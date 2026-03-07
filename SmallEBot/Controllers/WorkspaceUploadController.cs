// SmallEBot/Controllers/WorkspaceUploadController.cs
using Microsoft.AspNetCore.Mvc;
using SmallEBot.Application.Contracts.Workspaces;

namespace SmallEBot.Controllers;

/// <summary>API controller for chunked workspace file upload.</summary>
[ApiController]
[Route("api/workspace/upload")]
[IgnoreAntiforgeryToken]
public class WorkspaceUploadController(IWorkspaceUploadService uploadService) : ControllerBase
{
    /// <summary>Starts a new chunked upload. Returns upload ID.</summary>
    [HttpPost("start")]
    public async Task<ActionResult<StartUploadResponse>> Start([FromBody] StartUploadRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.FileName))
            return BadRequest("FileName is required");
        try
        {
            var uploadId = await uploadService.StartUploadAsync(request.FileName, request.ContentLength, ct);
            return Ok(new StartUploadResponse(uploadId));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Appends a chunk of data to an in-progress upload.</summary>
    [HttpPost("chunk/{uploadId}")]
    public async Task<IActionResult> Chunk(string uploadId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(uploadId))
            return BadRequest("UploadId is required");
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms, ct);
        var chunk = ms.ToArray();
        await uploadService.ReportChunkAsync(uploadId, chunk, ct);
        return NoContent();
    }

    /// <summary>Completes an upload and returns the result.</summary>
    [HttpPost("complete/{uploadId}")]
    public async Task<ActionResult<UploadCompleteResult>> Complete(string uploadId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(uploadId))
            return BadRequest("UploadId is required");
        var result = await uploadService.CompleteUploadAsync(uploadId, ct);
        if (result == null)
            return NotFound("Upload not found");
        return Ok(result);
    }

    /// <summary>Cancels an in-progress upload.</summary>
    [HttpPost("cancel/{uploadId}")]
    public IActionResult Cancel(string uploadId)
    {
        if (string.IsNullOrEmpty(uploadId))
            return BadRequest("UploadId is required");
        uploadService.CancelUpload(uploadId);
        return NoContent();
    }
}

public record StartUploadRequest(string FileName, long ContentLength);
public record StartUploadResponse(string UploadId);
