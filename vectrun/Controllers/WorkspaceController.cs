namespace vectrun.Controllers;

using Microsoft.AspNetCore.Mvc;
using vectrun.Models.Api;
using vectrun.Services;

[ApiController, Route("api/workspace")]
public class WorkspaceController(WorkspaceService workspaceService) : ControllerBase
{
    [HttpGet]
    public IActionResult Get([FromQuery] string directory) =>
        Ok(workspaceService.Load(directory));

    [HttpPost("scaffold")]
    public IActionResult Scaffold([FromBody] ScaffoldRequest req) =>
        Ok(workspaceService.Scaffold(req.Directory));

    [HttpPut]
    public IActionResult Save([FromBody] SaveWorkspaceRequest req)
    {
        workspaceService.Save(req);

        return Ok();
    }
}
