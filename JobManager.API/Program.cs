using Amazon;
using JobManager.API.Entities;
using JobManager.API.Persistance;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Configuration.AddSystemsManager(source =>
{
    source.AwsOptions = new Amazon.Extensions.NETCore.Setup.AWSOptions
    {
        Region = Amazon.RegionEndpoint.USEast2 // Change to your region
    };

    source.Path = "/";
    source.ReloadAfter = TimeSpan.FromMinutes(1);
});

//builder.Configuration.AddSecretsManager(null, RegionEndpoint.USEast2, config =>
//{
//    config.KeyGenerator = (Secret, name) => name.Replace("/", ":");
//    config.PollingInterval = TimeSpan.FromMinutes(5);
//});

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var connectionString = builder.Configuration.GetConnectionString("AppDb");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapPost("/api/jobs", async (Job job, AppDbContext db) =>
{
    await db.Jobs.AddAsync(job);
    await db.SaveChangesAsync();
    return Results.Created($"/api/jobs/{job.Id}", job);
});

app.MapGet("/api/jobs/{id}", async (int id, AppDbContext db) =>
{
    var job = await db.Jobs.FindAsync(id);
    if (job == null)
    {
        return Results.NotFound();
    }
    return Results.Ok(job);
});

app.MapGet("/api/jobs", async (AppDbContext db) =>
{
    var jobs = await db.Jobs.ToListAsync();
    return Results.Ok(jobs);
});

app.MapPost("/api/jobs/{jobId}/applications", async (int jobId, JobApplication application, [FromServices] AppDbContext db) =>
{
    var job = await db.Jobs.FindAsync(jobId);
    if (job == null)
    {
        return Results.NotFound();
    }

    application.JobId = jobId;
    await db.JobApplications.AddAsync(application);
    await db.SaveChangesAsync();
    return Results.Created($"/api/jobs/{jobId}/applications/{application.Id}", application);
});

app.MapPut("/api/applications/{id}", async (int id, IFormFile file, [FromServices] AppDbContext db) =>
{
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest("File is required.");
    }

    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
    var validExtensions = new[] { ".pdf", ".docx", ".txt" };

    if (!validExtensions.Contains(extension))
    {
        return Results.BadRequest("Invalid file type. Only PDF, DOCX, and TXT files are allowed.");
    }


    var application = await db.JobApplications.SingleOrDefaultAsync(a => a.Id == id);
    if (application == null)
    {
        return Results.NotFound();
    }

    var key = $"job-applications/{id}-{file.FileName}";

    application.CVUrl = key;
        
    db.JobApplications.Update(application);
    await db.SaveChangesAsync();
        
    return Results.Ok(application);
});

app.MapPost("/api/jobs/{jobId}/applications/{applicationId}/cv", async (int jobId, int applicationId, IFormFile cvFile, [FromServices] AppDbContext db) =>
{
    var application = await db.JobApplications.FindAsync(applicationId);
    if (application == null || application.JobId != jobId)
    {
        return Results.NotFound();
    }
    // Here you would typically upload the CV to S3 and get the URL back
    // For simplicity, we will just simulate this
    application.CVUrl = $"https://s3.example.com/cvs/{cvFile.FileName}";
    
    db.JobApplications.Update(application);
    await db.SaveChangesAsync();
    
    return Results.Ok(application);
});

//app.MapControllers();

app.Run();
