using BB.Api.Services;
using BBIntegration.Commits;
using BBIntegration.Common;
using BBIntegration.PullRequests;
using BBIntegration.Repositories;
using BBIntegration.Users;

var builder = WebApplication.CreateBuilder(args);

// 1. Create BitbucketConfig from appsettings.json
var bitbucketConfig = new BitbucketConfig
{
    DbConnectionString = builder.Configuration.GetConnectionString("DefaultConnection"),
    BitbucketApiBaseUrl = builder.Configuration["Bitbucket:ApiBaseUrl"],
    BitbucketConsumerKey = builder.Configuration["Bitbucket:ConsumerKey"],
    BitbucketConsumerSecret = builder.Configuration["Bitbucket:ConsumerSecret"]
};

// 2. Register config and services for Dependency Injection
builder.Services.AddSingleton(bitbucketConfig);
// The ApiClient must be a singleton to manage the lifecycle of the access token
builder.Services.AddSingleton<BitbucketApiClient>();
builder.Services.AddScoped<BitbucketUsersService>();
builder.Services.AddScoped<BitbucketRepositoriesService>();
builder.Services.AddScoped<BitbucketCommitsService>();
builder.Services.AddScoped<BitbucketPullRequestsService>();
builder.Services.AddScoped<AnalyticsService>();

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run(); 