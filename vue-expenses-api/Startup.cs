using System.Collections.Generic;
using System.Data;
using System.Net.NetworkInformation;
using System.Text.Json.Serialization;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using vue_expenses_api.Infrastructure;
using vue_expenses_api.Infrastructure.Security;
using vue_expenses_api.Infrastructure.Validation;

namespace vue_expenses_api;

public class Startup
{
    public Startup(
        IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public IConfiguration Configuration { get; }

    public void ConfigureServices(
        IServiceCollection services)
    {
        services.AddControllers()
            .AddNewtonsoftJson();

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Startup).Assembly));

        //hook up validation into MediatR pipeline
        services.AddTransient(
            typeof(IPipelineBehavior<,>),
            typeof(ValidationPipelineBehavior<,>));
        services.AddTransient<IDbConnection>(
            db => new SqliteConnection(
                Configuration.GetConnectionString("DefaultConnection")));

        services
            .AddEntityFrameworkSqlite()
            .AddDbContext<ExpensesContext>();

        //Hook up swagger
        services.AddSwaggerGen(
            x =>
            {
                x.AddSecurityDefinition(
                    "Bearer",
                    new OpenApiSecurityScheme
                    {
                        Description =
                            "JWT Authorization header using the Bearer scheme. \r\n\r\n Enter 'Bearer' [space] and then your token in the text input below.\r\n\r\nExample: \"Bearer 12345abcdef\"",
                        Name = "Authorization",
                        In = ParameterLocation.Header,
                        Type = SecuritySchemeType.ApiKey,
                        Scheme = "bearer"
                    });

                var requirement = new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Id = "Bearer", //The name of the previously defined security scheme.
                                Type = ReferenceType.SecurityScheme
                            }
                        },
                        new List<string>()
                    }
                };
                x.AddSecurityRequirement(
                    requirement);
                x.SwaggerDoc(
                    "v1",
                    new OpenApiInfo { Title = "Expenses API", Version = "v1" });
                x.CustomSchemaIds(y => y.FullName);
                x.DocInclusionPredicate(
                    (
                        version,
                        apiDescription) => true);
            });

        //attach the the model validator and define the api grouping convention
        //setup fluent validation for the running assembly
        services.AddMvc(
                options =>
                {
                    options.Filters.Add<ValidateModelFilter>();
                    options.Conventions.Add(new GroupByApiRootConvention());
                })
            .AddJsonOptions(
                opt => { opt.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull; });
        services.AddValidatorsFromAssemblyContaining<Startup>();
        services.Configure<JwtSettings>(Configuration.GetSection(nameof(JwtSettings)));
        services.Configure<PasswordHasherSettings>(Configuration.GetSection(nameof(PasswordHasherSettings)));

        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

        services.AddCors();
        services.AddJwt();
    }

    public void Configure(
        IApplicationBuilder app,
        IWebHostEnvironment env,
        ILoggerFactory loggerFactory)
    {
        loggerFactory.AddSerilogLogging();
        app.UseMiddlewares();

        if (!env.IsDevelopment())
        {
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        app.UseCors(
            builder =>
                builder
                    .AllowAnyOrigin()
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .WithExposedHeaders("Token-Expired"));

        app.UseSwagger(c => { c.RouteTemplate = "swagger/{documentName}/swagger.json"; });

        app.UseSwaggerUI(
            x =>
            {
                x.SwaggerEndpoint(
                    "/swagger/v1/swagger.json",
                    "Expenses API V1");
            });

        app.UseRouting();
        app.UseAuthorization();
        app.UseEndpoints(
            endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapFallbackToController(
                    "Index",
                    "Home");
            });

        app.UseDefaultFiles();
        app.UseStaticFiles();
    }
}