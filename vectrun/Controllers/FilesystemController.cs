namespace vectrun.Controllers;

using Microsoft.AspNetCore.Mvc;
using vectrun.Services;

[ApiController, Route("api/browse")]
public class FilesystemController(FilesystemService filesystemService) : ControllerBase
{
    [HttpGet]
    public IActionResult Browse([FromQuery] string? path) =>
        Ok(filesystemService.Browse(path));
}
