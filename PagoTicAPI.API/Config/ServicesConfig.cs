namespace PagoTicAPI.API.Config;

public static class ServicesConfig
{
    public static void AddConfig(this IServiceCollection services, IConfiguration configuration)
    {
        ValidateSecurityConfiguration(configuration);
        services.AddHttpContextAccessor();
        // services.AddSignalR();
        services.AddMemoryCache();

        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();

        services.AddCors(options =>
        {
            options.AddDefaultPolicy(builder =>
            {
                if (allowedOrigins != null && allowedOrigins.Any())
                {
                    builder.WithOrigins(allowedOrigins)
                           .AllowAnyMethod()
                           .AllowAnyHeader()
                           .AllowCredentials();
                }
            });
        });

        // QuestPDF.Settings.License = LicenseType.Community;

        services.AddSwagger();

        services.AddJwt(configuration);

        // services.AddInternalServices();
        services.BindAppSettings(configuration);
        // services.AddHealthChecks(configuration);

        // Base de datos Oracle — Gateway
        services.AddDbContext<gtwContext>(options =>
            options.UseOracle(configuration.GetConnectionString("Gateway")));

        services.AddPayPerTicClients();
        services.AddPayPerTicServices();
        services.AddAutomaticDebit(configuration);
        services.AddScoped<
            PagoTicAPI.Application.Services.Interfaces.IOperationalAuditService,
            PagoTicAPI.Application.Services.Implementations.OperationalAuditService>();

        services.AddControllers();

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());

        // services.AddConfiguration(configuration);
    }

    // private static void AddHealthChecks(this IServiceCollection services, IConfiguration configuration)
    // {
    //     services.AddHealthChecks()
    //         .AddCheck<DatabaseFacturacionHealthCkeck>(nameof(DatabaseFacturacionHealthCkeck));
    // }

    private static void AddJwt(this IServiceCollection services, IConfiguration configuration)
    {
        var secretKey = configuration["Jwt:SecretKey"]!;
        _ = services.AddAuthentication(x =>
        {
            x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        }).AddJwtBearer(options =>
        {
            options.RequireHttpsMetadata = false;
            options.SaveToken = true;
            options.TokenValidationParameters = new TokenValidationParameters()
            {
                RequireExpirationTime = true,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = configuration["Jwt:Issuer"] ?? "PagoTicAPI",
                ValidAudience = configuration["Jwt:Audience"] ?? "PagoTicAPI",
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(secretKey)),
                ClockSkew = TimeSpan.Zero,
            };

            options.Events = new JwtBearerEvents()
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;

                    // Configurar si usas SignalR:
                    // if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hub/dashboard"))
                    // {
                    //     context.Token = accessToken;
                    // }
                    return Task.CompletedTask;
                },

                OnChallenge = context =>
                {
                    context.HandleResponse();
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.ContentType = "application/json";

                    return context.Response.WriteAsync(JsonSerializer.Serialize(
                        OperationResponse<object>.CreateBuilder().WithCode(401)
                            .WithMessage("No estás autenticado.").Build()));
                },

                OnForbidden = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json";
                    return context.Response.WriteAsync(JsonSerializer.Serialize(
                        OperationResponse<object>.CreateBuilder().WithCode(403)
                            .WithMessage("No tienes permisos para realizar esta acción.").Build()));
                },
            };
        });
    }

    private static void ValidateSecurityConfiguration(IConfiguration configuration)
    {
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
        if (allowedOrigins is null || allowedOrigins.Length == 0 ||
            allowedOrigins.Any(origin => !Uri.TryCreate(origin, UriKind.Absolute, out _)))
        {
            throw new InvalidOperationException(
                "Cors:AllowedOrigins must contain at least one valid absolute origin.");
        }

        var secretKey = configuration["Jwt:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey) || secretKey.Length < 32 ||
            string.Equals(secretKey, "YourSecretKeyHere12345", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Jwt:SecretKey must be explicitly configured with at least 32 characters.");
        }
    }

    // private static void AddConfiguration(this IServiceCollection services, IConfiguration configuration)
    // {
    //     // Sequence.Initialize(configuration);
    //     // var context = services.BuildServiceProvider().GetRequiredService<FacturacionContext>();
    // }

    private static void AddSwagger(this IServiceCollection services)
    {
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "PagoTicAPI API",
                Version = "v1"
            });

            c.DocumentFilter<PagoTicAPI.API.OpenApi.HealthChecksDocumentFilter>();

            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Description = "Ingrese el token de autenticación en el siguiente formato: Bearer {token}",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
                BearerFormat = "JWT",
                Scheme = "Bearer"
            });

            // c.OperationFilter<FileUploadOperationFilter>();

            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });

            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            // c.IncludeXmlComments(xmlPath);
        });
    }

    // private static void AddInternalServices(this IServiceCollection services)
    // {
    //     // Ejemplo de cómo registrar servicios:
    //     // services.AddScoped<IFacturaService, FacturaService>();
    //     // services.AddScoped<IAuthService, AuthService>();
    //     
    //     // Validadores:
    //     // services.AddTransient<IValidator<RegistroUsuarioRequestDto>, AddUsuarioValidator>();
    // }

    private static void BindAppSettings(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPerTicKeys>(configuration.GetSection("PayPerTicKeys"));
    }

    private static void AddPayPerTicClients(this IServiceCollection services)
    {
        // Auth client: obtiene y cachea el Bearer token OAuth2 de PayPerTIC.
        services.AddHttpClient<IPayPerTicAuthClient, PayPerTicAuthClient>();

        // Payment client: realiza las llamadas a /pagos, /pagos/cancelar, /pagos/devolucion.
        // Depende de IPayPerTicAuthClient para inyectar el token en cada request.
        services.AddHttpClient<IPayPerTicClient, PayPerTicClient>();
    }

    private static void AddPayPerTicServices(this IServiceCollection services)
    {
        services.AddScoped<IPayPerTicService, PayPerTicService>();
    }
}
