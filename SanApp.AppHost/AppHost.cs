var builder = DistributedApplication.CreateBuilder(args);

var applicationDatabase = builder.AddPostgres("application-database")
    .WithDataVolume()
    .AddDatabase("sanapp");

var migrationGateDatabase = builder.AddPostgres("migration-gate-database")
    .WithDataVolume()
    .AddDatabase("sanapp-migration-gate");

builder.AddProject<Projects.SanApp>("sanapp")
    .WithReference(applicationDatabase)
    .WithReference(migrationGateDatabase)
    .WaitFor(applicationDatabase)
    .WaitFor(migrationGateDatabase);

builder.Build().Run();
