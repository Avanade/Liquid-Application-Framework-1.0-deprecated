using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System;
using System.Threading.Tasks;

namespace Liquid.Microservices.Runtime.Telemetry
{
    /// <summary>
    /// Cria uma extensão para o middleware
    /// </summary>
    public static class TelemetryExtensions
    {
        /// <summary>
        /// Ativa o middleware customizado AppInsightsTelemetry para capturar todos os eventos das API.
        /// e registrar logg no AppInsights com os detalhes adequados
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static IApplicationBuilder UseTelemetry(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<AppInsightsTelemetryMiddleware>();
        }
    }

    /// <summary>
    /// This telemetry don't will use the TelemetryClient because we need centrilaze all messages to AppInsights
    /// </summary>
    public class AppInsightsTelemetryMiddleware
    {
        private readonly LightTelemetry _telemetry = (LightTelemetry)WorkBench.Telemetry;

        private readonly RequestDelegate _next;

        /// <summary>
        /// Middleware de Telemetria de Exceções 500
        /// </summary>
        /// <param name="next"></param>
        public AppInsightsTelemetryMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        /// <summary>
        /// Intercepta a chamada da API
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task Invoke(HttpContext context)
        {
            try
            {
                //TODO: Write log's to AppInsights or another techonology.
                //TODO: Why trackEvent for every rest call?! 
                //_telemetry.TrackEvent($"Telemetry from middleware.");

                await _next(context);
            }
            catch (Exception ex)
            {
                _telemetry.TrackException(ex);
                throw ex; //TODO: Check if its correcty
            }
        }
    }
}
