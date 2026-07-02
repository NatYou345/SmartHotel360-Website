using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SpaServices.ReactDevelopmentServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using SmartHotel360.PublicWeb.Models.Settings;
using SmartHotel360.PublicWeb.Services;
using System;

namespace SmartHotel360.PublicWeb
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddMvc(options => options.EnableEndpointRouting = false);
            builder.Services.AddOptions();
            builder.Services.Configure<LocalSettings>(builder.Configuration);
            builder.Services.AddSingleton<SettingsService>(sp => SettingsService.Load(sp.GetService<IOptions<LocalSettings>>().Value));

            if (!string.IsNullOrEmpty(builder.Configuration["USE_NULL_TESTIMONIALS_SERVICE"]))
            {
                builder.Services.AddSingleton<ICustomerTestimonialService>(new NullCustomerTestimonialService());
            }
            else
            {
                builder.Services.AddSingleton<ICustomerTestimonialService, PositiveTweetService>();
            }

            var app = builder.Build();

            if (app.Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }

            app.UseStaticFiles();

            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Home}/{action=Index}/{id?}");
            });

            app.UseSpa(spa =>
            {
                spa.Options.SourcePath = "ClientApp";

                if (app.Environment.IsDevelopment())
                {
                    spa.UseReactDevelopmentServer(npmScript: "start");
                }
            });

            app.Run();
        }
    }
}
