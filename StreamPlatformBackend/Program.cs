using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Npgsql;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.Services;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Добавляем поддержку JSON
builder.Services.AddControllers();

// ⭐ ДОБАВЛЯЕМ SWAGGER ⭐
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Stream Platform API",
        Version = "v1",
        Description = "API for streaming platform"
    });

    // Добавляем поддержку JWT в Swagger
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
            new string[] {}
        }
    });
});

// Добавляем БД
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Регистрация сервисов
builder.Services.AddScoped<IPasswordHasherService, PasswordHasherService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IJwtService, JwtService>();

builder.Services.AddScoped<IStreamService, StreamService>();

// Настройка аутентификации
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var  secretKey = builder.Configuration["Jwt:SecretKey"] ?? "fallback-secret-key-minimum-32-chars";

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

var app = builder.Build();

// ⭐ ВКЛЮЧАЕМ SWAGGER ⭐
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Stream Platform API v1");
        options.RoutePrefix = "swagger"; // Теперь Swagger будет доступен по /swagger
    });
}

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}


using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<AppDbContext>();

        // Проверяем существование базы данных и создаем если нет
        //context.Database.EnsureCreated();

        // Или используйте миграции (рекомендуется)
        context.Database.Migrate();

        Console.WriteLine("Database created successfully");
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while creating the database");

        // Альтернативный способ: создаем базу через прямое подключение
        CreateDatabaseIfNotExists(builder.Configuration);
    }
}

// Метод для создания базы данных
static void CreateDatabaseIfNotExists(IConfiguration configuration)
{
    var connectionString = configuration.GetConnectionString("DefaultConnection");
    var databaseName = "StreamDB";

    // Создаем строку подключения к postgres (системная БД)
    var masterConnectionString = connectionString.Replace(databaseName, "postgres");

    using var connection = new NpgsqlConnection(masterConnectionString);
    connection.Open();

    // Проверяем существование базы данных
    using var command = new NpgsqlCommand(
        $"SELECT 1 FROM pg_database WHERE datname = '{databaseName}'", connection);
    var exists = command.ExecuteScalar() != null;

    if (!exists)
    {
        using var createCommand = new NpgsqlCommand(
            $"CREATE DATABASE \"{databaseName}\"", connection);
        createCommand.ExecuteNonQuery();
        Console.WriteLine($"Database {databaseName} created successfully");
    }
}



app.UseRouting();

app.UseCors("AllowAll");
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();