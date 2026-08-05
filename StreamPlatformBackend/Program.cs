using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using StackExchange.Redis;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.Hubs;
using StreamPlatformBackend.Models.Enums;
using StreamPlatformBackend.Services;
using StreamPlatformBackend.Services.NotificationService;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// Добавляем поддержку JSON и контроллеров
builder.Services.AddControllers();

// ⭐ Swagger с поддержкой XML комментариев ⭐
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Stream Platform API",
        Version = "v1",
        Description = "API for streaming platform"
    });

    // Поддержка JWT
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
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

    // Подключаем XML комментарии
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    options.IncludeXmlComments(xmlPath);
});



// Добавляем DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));


// Регистрация сервисов
builder.Services.AddScoped<IPasswordHasherService, PasswordHasherService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<INotificationSender, NotificationSender>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IStreamService, StreamService>();
builder.Services.AddScoped<IStreamLiveNotifier, StreamLiveNotifier>();
builder.Services.AddScoped<IChatNicknameNotifier, ChatNicknameNotifier>();



builder.Services.AddScoped<IRedisChatService, RedisChatService>();
builder.Services.AddScoped<IStreamChatBanService, StreamChatBanService>();
builder.Services.AddScoped<IStreamTeamService, StreamTeamService>();
builder.Services.AddScoped<IStreamChatModerationLogService, StreamChatModerationLogService>();
builder.Services.AddScoped<IStreamDashboardService, StreamDashboardService>();
builder.Services.AddScoped<ICategoryBannerSeedService, CategoryBannerSeedService>();
builder.Services.AddScoped<IStaffAuditService, StaffAuditService>();
builder.Services.AddScoped<IStaffUserService, StaffUserService>();
builder.Services.AddScoped<IStaffStatsService, StaffStatsService>();
builder.Services.AddScoped<IPlatformSanctionService, PlatformSanctionService>();
builder.Services.AddScoped<IPlatformReportService, PlatformReportService>();
builder.Services.AddScoped<ISupportTicketService, SupportTicketService>();
builder.Services.AddScoped<IPlatformAppealService, PlatformAppealService>();
builder.Services.AddHostedService<CategoryBannerSeedHostedService>();
builder.Services.AddHostedService<SanctionExpireHostedService>();



builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
});

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var redis = builder.Configuration.GetConnectionString("Redis")
        ?? throw new InvalidOperationException("ConnectionStrings:Redis is not configured (set appsettings.Local.json or env)");
    return ConnectionMultiplexer.Connect(redis);
});

// Behind nginx: prefer X-Real-IP / X-Forwarded-For for per-client rate limits.
static string ClientIp(HttpContext httpContext)
{
    var forwarded = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(forwarded))
        return forwarded.Split(',')[0].Trim();

    var realIp = httpContext.Request.Headers["X-Real-IP"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(realIp))
        return realIp.Trim();

    return httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            message = "Слишком много запросов. Подождите минуту и попробуйте снова."
        }, cancellationToken);
    };

    // login / register — anti brute-force
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ClientIp(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    // public search / autocomplete (when endpoints exist)
    options.AddPolicy("search", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ClientIp(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    // stream-key, regenerate key, reports
    options.AddPolicy("sensitive", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ClientIp(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});


// Аутентификация JWT
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var secretKey = builder.Configuration["Jwt:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey) || secretKey.Length < 32)
            throw new InvalidOperationException("Jwt:SecretKey must be configured in appsettings.Local.json or env (min 32 chars)");

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "StreamPlatformBackend",
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "StreamPlatformUsers",
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
            ClockSkew = TimeSpan.Zero
        };

        // IMPORTANT: allow JWT in querystring for SignalR websocket transport
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                // Try get token from query string for SignalR websocket requests
                var accessToken = context.Request.Query["access_token"].FirstOrDefault();
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("StaffOnly", policy =>
        policy.RequireRole(UserRole.StaffRoles));

    options.AddPolicy("StaffModerator", policy =>
        policy.RequireRole(UserRole.PlatformModeratorRoles));

    options.AddPolicy("StaffAdmin", policy =>
        policy.RequireRole(UserRole.AdminRoles));
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(10);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(10);
});




// Настройка CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});



// WebSocket / SignalR
builder.Services.AddSignalR()
    .AddStackExchangeRedis(builder.Configuration.GetConnectionString("Redis"));



builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

var app = builder.Build();

// ⭐ Swagger и DevExceptionPage только на Development ⭐
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Stream Platform API v1");
        options.RoutePrefix = "swagger"; // Swagger доступен по /swagger
    });
}


// Инициализация БД
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<AppDbContext>();
        context.Database.Migrate();
        Console.WriteLine("Database migrated successfully");

        var banService = services.GetRequiredService<IStreamChatBanService>();
        await banService.SyncAllToRedisAsync();
        Console.WriteLine("Chat bans synced to Redis");

        var teamService = services.GetRequiredService<IStreamTeamService>();
        await teamService.SyncAllToRedisAsync();
        Console.WriteLine("Stream team synced to Redis");
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Ошибка при миграции базы данных");
    }
}

app.UseForwardedHeaders();
app.UseRouting();
app.UseCors("AllowAll");
app.UseRateLimiter();

app.UseHttpsRedirection();

var mediaPath = builder.Configuration["Media:Path"];

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(mediaPath),
    RequestPath = "/media"
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<StreamHub>("/hubs/streamHub");
app.MapHub<NotificationHub>("/hubs/notificationHub");

app.Run();