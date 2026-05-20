using Microsoft.AspNetCore.Mvc;
using SphereAlert.Services.Scripts;

namespace SphereAlert.Controllers
{
    /// <summary>
    /// Serves the client banner script and the container health endpoint — the only
    /// two routes that must be reachable without a session.
    /// </summary>
    [ApiController]
    public class ScriptController : ControllerBase
    {
        private readonly ScriptService _scriptService;

        public ScriptController(ScriptService scriptService)
        {
            _scriptService = scriptService;
        }

        /// <summary>The drop-in client script. Operators grab this straight from their container.</summary>
        [HttpGet("/js/sphere-alert.js")]
        public IActionResult GetScript()
        {
            Response.Headers.CacheControl = "public, max-age=3600";
            return File(_scriptService.Bytes, "application/javascript; charset=utf-8");
        }

        /// <summary>Container healthcheck.</summary>
        [HttpGet("/healthz")]
        public IActionResult Health() => Ok("ok");
    }
}
