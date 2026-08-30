using System.Net;
using System.Net.Http.Json;
using Lims.Core.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Lims.Tests.Integration;

/// <summary>
/// End-to-end tests of the full HTTP pipeline: routing, JWT bearer
/// authentication, role-based authorization and token revocation -
/// the complete login -> 401/403/204 flow, without a database.
/// </summary>
public class AuthFlowIntegrationTests : IClassFixture<LimsApiFactory>
{
    private readonly LimsApiFactory _factory;

    public AuthFlowIntegrationTests(LimsApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Anonymous_request_is_rejected_with_401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/samples");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_with_unknown_user_returns_401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "ghost", password = "Whatever@1" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "analyst1", password = "WrongPass@1" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_with_valid_credentials_returns_token_and_role()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "analyst1", password = "Analyst@2026" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>();
        Assert.Equal("analyst1", payload!["username"].GetString());
        Assert.Equal(UserRoles.Analyst, payload["role"].GetString());
        Assert.False(string.IsNullOrWhiteSpace(payload["token"].GetString()));
    }

    [Fact]
    public async Task Analyst_cannot_change_sample_status_403_but_manager_can_204()
    {
        var analystToken = await _factory.LoginAsync("analyst1", "Analyst@2026");
        var managerToken = await _factory.LoginAsync("qual.manager", "Manager@2026");

        // Analyst -> forbidden (Manager-only transition)
        var analystClient = _factory.CreateClientWithToken(analystToken);
        var forbidden = await analystClient.PutAsJsonAsync("/api/samples/SMP-2026-00001/status",
            new { newStatus = SampleStatus.Validated, comment = "not allowed" });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        // Manager -> allowed transition COMPLETED -> VALIDATED
        var managerClient = _factory.CreateClientWithToken(managerToken);
        var ok = await managerClient.PutAsJsonAsync("/api/samples/SMP-2026-00001/status",
            new { newStatus = SampleStatus.Validated, comment = "QC reviewed" });
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);

        // the fake store reflects the transition
        var sample = await _factory.Samples.GetSampleByCodeAsync("SMP-2026-00001");
        Assert.Equal(SampleStatus.Validated, sample!.Status);
    }

    [Fact]
    public async Task Analyst_result_submission_returns_200()
    {
        var analystToken = await _factory.LoginAsync("analyst1", "Analyst@2026");
        var client = _factory.CreateClientWithToken(analystToken);

        var response = await client.PostAsJsonAsync("/api/samples/SMP-2026-00001/results",
            new { testCode = "PH", resultValue = 7.2, source = "REST_API" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ResultSubmissionResult>();
        Assert.True(result!.Passed);
    }

    [Fact]
    public async Task Analyst_cannot_create_users_403()
    {
        var analystToken = await _factory.LoginAsync("analyst1", "Analyst@2026");
        var client = _factory.CreateClientWithToken(analystToken);

        var response = await client.PostAsJsonAsync("/api/users",
            new { username = "sneaky", displayName = "Sneaky User", role = UserRoles.Manager, password = "Sneaky@123" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Manager_can_create_user_who_then_logs_in()
    {
        var managerToken = await _factory.LoginAsync("qual.manager", "Manager@2026");
        var managerClient = _factory.CreateClientWithToken(managerToken);

        var create = await managerClient.PostAsJsonAsync("/api/users",
            new { username = "new.analyst", displayName = "New Analyst", role = UserRoles.Analyst, password = "Fresh@2026" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var login = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { username = "new.analyst", password = "Fresh@2026" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Change_password_revokes_tokens_issued_before_the_change()
    {
        // dedicated account, created on the fly through the admin endpoint
        var managerToken = await _factory.LoginAsync("qual.manager", "Manager@2026");
        var managerClient = _factory.CreateClientWithToken(managerToken);
        await managerClient.PostAsJsonAsync("/api/users",
            new { username = "pwd.analyst", displayName = "Pwd Analyst", role = UserRoles.Analyst, password = "OldPass@2026" });

        var oldToken = await _factory.LoginAsync("pwd.analyst", "OldPass@2026");
        var oldClient = _factory.CreateClientWithToken(oldToken);
        Assert.Equal(HttpStatusCode.OK, (await oldClient.GetAsync("/api/auth/me")).StatusCode);

        // self-service password change
        var change = await oldClient.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = "OldPass@2026", newPassword = "NewPass@2026" });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        // the old token is now rejected by the TokenVersion check
        var afterChange = await oldClient.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, afterChange.StatusCode);

        // the new password works
        var newLogin = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { username = "pwd.analyst", password = "NewPass@2026" });
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_presented_token()
    {
        var token = await _factory.LoginAsync("analyst1", "Analyst@2026");
        var client = _factory.CreateClientWithToken(token);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/samples")).StatusCode);
    }

    [Fact]
    public async Task Deactivated_user_tokens_are_rejected_immediately()
    {
        var managerToken = await _factory.LoginAsync("qual.manager", "Manager@2026");
        var managerClient = _factory.CreateClientWithToken(managerToken);

        await managerClient.PostAsJsonAsync("/api/users",
            new { username = "temp.analyst", displayName = "Temp Analyst", role = UserRoles.Analyst, password = "Temp@2026" });

        var token = await _factory.LoginAsync("temp.analyst", "Temp@2026");
        var tempClient = _factory.CreateClientWithToken(token);
        Assert.Equal(HttpStatusCode.OK, (await tempClient.GetAsync("/api/auth/me")).StatusCode);

        var user = (await _factory.Users.GetByUsernameAsync("temp.analyst"))!;
        var deactivate = await managerClient.PutAsJsonAsync($"/api/users/{user.UserId}/active", new { isActive = false });
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await tempClient.GetAsync("/api/auth/me")).StatusCode);

        // login is also refused for deactivated accounts
        var login = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { username = "temp.analyst", password = "Temp@2026" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }
}