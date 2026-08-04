using System;
using System.Net;
using System.Threading.Tasks;
using ArchiSteamFarm.IPC.Controllers.Api;
using ArchiSteamFarm.IPC.Responses;
using Microsoft.AspNetCore.Mvc;

namespace IpcConfig;

[Route("Api/IpcConfig")]
public sealed class IpcConfigController : ArchiController {
	private readonly IpcConfigFileService FileService;

	public IpcConfigController(IpcConfigFileService fileService) =>
		FileService = fileService ?? throw new ArgumentNullException(nameof(fileService));

	[HttpGet]
	[ProducesResponseType<GenericResponse<IpcConfigStatusResponse>>((int) HttpStatusCode.OK)]
	public async Task<ActionResult<GenericResponse<IpcConfigStatusResponse>>> Get() {
		IpcConfigStatusResponse status = await FileService.GetStatusAsync().ConfigureAwait(false);

		return Ok(new GenericResponse<IpcConfigStatusResponse>(status));
	}

	[HttpPut]
	[ProducesResponseType<GenericResponse<IpcConfigStatusResponse>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	public async Task<ActionResult<GenericResponse<IpcConfigStatusResponse>>> Put([FromBody] IpcConfigWriteRequest? request) {
		if (request == null) {
			return BadRequest(new GenericResponse(false, "Request body is required."));
		}

		try {
			IpcConfigStatusResponse status = await FileService.WriteAsync(request).ConfigureAwait(false);

			return Ok(new GenericResponse<IpcConfigStatusResponse>(status));
		} catch (ArgumentException e) {
			return BadRequest(new GenericResponse(false, e.Message));
		} catch (InvalidOperationException e) {
			return BadRequest(new GenericResponse(false, e.Message));
		}
	}

	[HttpDelete]
	[ProducesResponseType<GenericResponse<IpcConfigStatusResponse>>((int) HttpStatusCode.OK)]
	public async Task<ActionResult<GenericResponse<IpcConfigStatusResponse>>> Delete() {
		await FileService.DeleteAsync().ConfigureAwait(false);

		IpcConfigStatusResponse status = IpcConfigStatusResponse.FromDefaults(FileService.AbsolutePath, fileExists: false);
		status.RestartRequired = true;

		return Ok(new GenericResponse<IpcConfigStatusResponse>(status));
	}
}
