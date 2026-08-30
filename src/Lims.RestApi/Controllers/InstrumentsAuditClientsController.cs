using Lims.Core.Interfaces;
using Lims.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lims.RestApi.Controllers;

// ============================================================
// GET /api/instruments  - all instruments + calibration status
// GET /api/audit        - paged audit trail (Manager only)
// GET /api/clients      - active clients for dropdowns
// ============================================================

/// <summary>
/// Instruments calibration status for all lab analysers.
/// Used by the Instruments panel and the instrument picker in result submission.
/// </summary>
[ApiController]
[Route("api/instruments")]
[Authorize]
[Produces("application/json")]
public class InstrumentsController : ControllerBase
{
    private readonly ISampleRepository _repo;
    public InstrumentsController(ISampleRepository repo) => _repo = repo;

    /// <summary>Returns all instruments ordered by calibration urgency (overdue first).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<Instrument>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<Instrument>>> GetAll()
        => Ok(await _repo.GetInstrumentsAsync());
}

/// <summary>
/// Audit trail: paged read-only view of dbo.AuditLog (Manager role only).
/// Covers all subsystems: REST_API, SOAP_SERVICE, WIN_SERVICE, SSIS.
/// </summary>
[ApiController]
[Route("api/audit")]
[Authorize(Roles = "Manager")]
[Produces("application/json")]
public class AuditController : ControllerBase
{
    private readonly ISqlAuditReader _reader;
    public AuditController(ISqlAuditReader reader) => _reader = reader;

    /// <summary>
    /// Search and page the audit log.
    /// Filters: free-text on Source/Action/EntityRef/Message, isSuccess, date range.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(AuditPagedResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<AuditPagedResult>> Search(
        [FromQuery] string?   searchText,
        [FromQuery] bool?     isSuccess,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] int page     = 1,
        [FromQuery] int pageSize = 50)
        => Ok(await _reader.SearchAuditLogAsync(searchText, isSuccess, fromDate, toDate, page, pageSize));
}

/// <summary>
/// Active client accounts for registration form dropdowns.
/// </summary>
[ApiController]
[Route("api/clients")]
[Authorize]
[Produces("application/json")]
public class ClientsController : ControllerBase
{
    private readonly ISqlAuditReader _reader;
    public ClientsController(ISqlAuditReader reader) => _reader = reader;

    /// <summary>Returns active clients (code + name) sorted by ClientCode.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ClientDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ClientDto>>> GetClients()
        => Ok(await _reader.GetClientsAsync());
}
