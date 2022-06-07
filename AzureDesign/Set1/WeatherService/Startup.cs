using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Polly;
using Polly.CircuitBreaker;
using WeatherService.Services;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using HealthChecks.UI.Client;

namespace WeatherService
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo {Title = "WeatherService", Version = "v1"});
            });
            var basicCircuitBreakerPolicy = Policy.HandleResult<HttpResponseMessage>
                                    (r=> r.StatusCode!= System.Net.HttpStatusCode.OK)
                                    .CircuitBreakerAsync(2,TimeSpan.FromSeconds(60),OnBreak,OnReset,OnHalfOpen);
            

            services.AddHttpClient<ITemperatureService,TemperatureService>("TemperatureServiceClient")
                    .AddPolicyHandler(basicCircuitBreakerPolicy)
                    .AddTransientHttpErrorPolicy(builder =>
                    builder.WaitAndRetryAsync(new[]
                    {
                        TimeSpan.FromMilliseconds(100),
                        TimeSpan.FromMilliseconds(500),
                    }));

            services.AddHealthChecks()
                .AddCheck("Temperature Service", () =>
                {
                    return basicCircuitBreakerPolicy.CircuitState switch
                    {
                        CircuitState.Open => HealthCheckResult.Unhealthy("Circuit Breaker is in Open State"),
                        CircuitState.HalfOpen => HealthCheckResult.Degraded("Circuit Breaker is in Half Open State"),
                        _ => HealthCheckResult.Healthy()
                    };
                });

            services.AddHealthChecksUI((settings => { settings.AddHealthCheckEndpoint("Weather Service", "/hc"); }))
                .AddInMemoryStorage();

        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseHttpsRedirection();
            app.UseSwagger();
            app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "WeatherService v1"));

            
            
            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHealthChecks("/hc", new HealthCheckOptions
                {
                    Predicate = _ => true,
                    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
                });

                endpoints.MapHealthChecks("/liveness", new HealthCheckOptions
                {
                    Predicate = r => r.Name.Contains("self")
                });

                endpoints.MapHealthChecksUI(options => { options.UIPath = "/hc-ui"; });

            });
        }


        private void OnHalfOpen()
        {
           Console.WriteLine("Circuit in test mode, one request will be allowed.");
        }

        private void OnReset()
        {
            Console.WriteLine("Circuit closed, requests flow normally.");
        }

        private void OnBreak(DelegateResult<HttpResponseMessage> result, TimeSpan ts)
        {
            Console.WriteLine("Circuit cut, requests will not flow.");
        }
    }
}
