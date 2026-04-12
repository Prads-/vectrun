namespace vectrun.Controllers;

using Microsoft.AspNetCore.Mvc;
using vectrun.Services;

[ApiController, Route("api/recents")]
public class RecentsController(RecentsService recentsService) : ControllerBase
{
    [HttpGet]
    public IActionResult GetAll() => Ok(recentsService.GetAll());

    [HttpPost]
    public IActionResult Add([FromBody] RecentPathRequest req) =>
        Ok(recentsService.Add(req.Path));

    [HttpDelete]
    public IActionResult Remove([FromBody] RecentPathRequest req) =>
        Ok(recentsService.Remove(req.Path));
}

public record RecentPathRequest(string Path);
