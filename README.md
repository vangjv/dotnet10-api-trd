# Reference .NET 10 Enterprise API Skeleton

The [reference solution](./reference-structure) implements a versioned customer read and display-name update through ASP.NET Core, Application, Domain, and EF Core with SQL Server. It includes default-deny API key authentication, Problem Details, OpenAPI, separate health probes, and canonical JSON console logs.

## Quick start (local)
From `reference-structure`, install the SDK selected by `global.json`, then run:

```powershell
dotnet restore
dotnet build --no-restore
dotnet test --no-build
```

To run the SQL integration test, set `REFERENCE_TEST_SQL_SERVER` to a **disposable SQL Server instance** using an account allowed to create and drop test databases. The test creates a uniquely named database and removes it afterward.

To run the API, set `ASPNETCORE_ENVIRONMENT=Development`, configure `ConnectionStrings:Default` and `ApiKey:Keys:0` with `dotnet user-secrets set <name> <value> --project .\src\Company.Product.Service.Api`, then run `dotnet run --project .\src\Company.Product.Service.Api --urls http://localhost:5080`. Use a randomly generated 32-byte hex key; never commit it. Apply the initial migration separately using `dotnet ef database update --project .\src\Company.Product.Service.Infrastructure --startup-project .\src\Company.Product.Service.Api` against your **local** database and insert a customer for the manual requests. OpenAPI is available at `/swagger/v1/swagger.json` in Development. See the [sample requests](./reference-structure/Customers.http) and [Fargate deployment notes](./reference-structure/AWS_FARGATE.md).

