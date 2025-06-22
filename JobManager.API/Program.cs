using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SQS;
using JobManager.API.Configuration;
using JobManager.API.DTO;
using JobManager.API.Entities;
using JobManager.API.Persistance;
using JobManager.API.Workers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static System.Net.Mime.MediaTypeNames;

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
var connectionStringSqs = builder.Configuration.GetConnectionString("Sqs");

builder.Services.AddSingleton(new SqsSettings
{
    QueueUrl = connectionStringSqs
});

builder.Services.AddHostedService<JobApplicationNotificationWorker>();

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
    var job = await db.Jobs.SingleOrDefaultAsync(j => j.Id == id);

    if (job is null)
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

app.MapPost("/api/jobs/{jobId}/job-applications", async (
    int jobId,
    JobApplicationRequest request,
    [FromServices] AppDbContext db,
    [FromServices] IConfiguration configuration
    ) =>
{
    var exists = await db.Jobs.AnyAsync(j => j.Id == jobId);

    if (!exists)
    {
        return Results.NotFound();
    }

    var application = new JobApplication
    {
        JobId = jobId,
        CandidateName = request.CandidateName,
        CandidateEmail = request.CandidateEmail,
        CVUrl = string.Empty // Pode deixar vazio até fazer o upload
    };

    await db.JobApplications.AddAsync(application);
    await db.SaveChangesAsync();

    var sqs = new AmazonSQSClient(RegionEndpoint.USEast2);
    var message = $"Nova candidatura para a vaga {jobId}:\n" +
                  $"Nome: {request.CandidateName}\n" +
                  $"Email: {request.CandidateEmail}";
    var requestSqs = new Amazon.SQS.Model.SendMessageRequest
    {
        QueueUrl = connectionStringSqs,
        MessageBody = System.Text.Json.JsonSerializer.Serialize(message)
    };

    var result = await sqs.SendMessageAsync(requestSqs);
    if (result.HttpStatusCode != System.Net.HttpStatusCode.OK)
    {
        return Results.StatusCode((int)result.HttpStatusCode);
    }
    return Results.NoContent();
});

app.MapPut("/api/job-applications/{id}/upload-cv", async (int id, IFormFile file, [FromServices] AppDbContext db) =>
{
    if (file == null || file.Length == 0)
    {
        return Results.BadRequest();
    }

    var extension = Path.GetExtension(file.FileName);

    var validExtensions = new List<string> { ".pdf", ".docx" };

    if (!validExtensions.Contains(extension))
    {
        return Results.BadRequest();
    }

    var client = new AmazonS3Client(RegionEndpoint.USEast2);

    var bucketName = "awstudys2";
    var key = $"job-applications/{id}-{file.FileName}";

    using var stream = file.OpenReadStream();

    var putObject = new PutObjectRequest
    {
        BucketName = bucketName,
        Key = key,
        InputStream = stream
    };

    var response = await client.PutObjectAsync(putObject);

    var application = await db.JobApplications.SingleOrDefaultAsync(ja => ja.Id == id);

    if (application is null)
    {
        return Results.NotFound();
    }

    application.CVUrl = key;

    await db.SaveChangesAsync();

    return Results.NoContent();
}).DisableAntiforgery();

app.MapGet("/api/job-applications/{id}/cv", async (int id, string email, [FromServices] AppDbContext db) =>
{
    var application = await db.JobApplications.FirstOrDefaultAsync(ja => ja.CandidateEmail == email);

    if (application is null)
    {
        return Results.NotFound();
    }

    var bucketName = "awstudys2";

    var getRequest = new GetObjectRequest
    {
        BucketName = bucketName,
        Key = application.CVUrl
    };

    var client = new AmazonS3Client(RegionEndpoint.USEast2);

    var response = await client.GetObjectAsync(getRequest);

    return Results.File(response.ResponseStream, response.Headers.ContentType);
});

//app.MapControllers();

app.Run();