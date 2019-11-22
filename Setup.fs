module Setup

open System
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging
open Microsoft.Extensions.DependencyInjection
open Giraffe
open Amazon.Lambda.Core
open Amazon.Lambda.APIGatewayEvents
open Amazon.Lambda.RuntimeSupport
open Amazon.Lambda.Serialization.Json
open System.Threading.Tasks



// ---------------------------------
// Config and Main
// ---------------------------------


let configureApp (app : IApplicationBuilder) =
    let env = app.ApplicationServices.GetService<IHostingEnvironment>()
    (match env.IsDevelopment() with
    | true  -> app.UseDeveloperExceptionPage()
    | false -> app.UseGiraffeErrorHandler AppHandlers.errorHandler)
        .UseStaticFiles()
        .UseGiraffe(AppHandlers.webApp)

let configureServices (services : IServiceCollection) =

    // To add AWS services to the ASP.NET Core dependency injection add
    // the AWSSDK.Extensions.NETCore.Setup NuGet package. Then
    // use the "AddAWSService" method to add AWS service clients.
    //
    // services.AddAWSService<Amazon.S3.IAmazonS3>() |> ignore

    services.AddGiraffe() |> ignore

let configureLogging (builder : ILoggingBuilder) =
    let filter (l : LogLevel) = l.Equals LogLevel.Error
    builder.AddFilter(filter).AddConsole().AddDebug() |> ignore

let configureAppConfiguration (ctx:WebHostBuilderContext) (builder : IConfigurationBuilder) =

    builder.AddJsonFile("appsettings.json", true, true)
        .AddJsonFile((sprintf "appsettings.%s.json" ctx.HostingEnvironment.EnvironmentName), true, true)
        .AddEnvironmentVariables() |> ignore

// ---------------------------------
// This type is the entry point when running in Lambda. It has similar responsiblities
// to the main entry point function that can be used for local development.
// ---------------------------------
type LambdaEntryPoint() =
    inherit Amazon.Lambda.AspNetCoreServer.APIGatewayProxyFunction()

    override this.Init(builder : IWebHostBuilder) =
        let contentRoot = Directory.GetCurrentDirectory()
        
        builder
            .UseContentRoot(contentRoot) 
            .Configure(Action<IApplicationBuilder> configureApp)
            .ConfigureServices(configureServices)
            |> ignore

let main _ =
    let useLambda = String.IsNullOrEmpty(Environment.GetEnvironmentVariable("AWS_LAMBDA_FUNCTION_NAME"))
    if useLambda then
        let contentRoot = Directory.GetCurrentDirectory()
        let webRoot     = Path.Combine(contentRoot, "WebRoot")
        WebHostBuilder()
            .UseContentRoot(contentRoot)
            .UseIISIntegration()
            .UseWebRoot(webRoot)
            .ConfigureAppConfiguration(Action<WebHostBuilderContext, IConfigurationBuilder> configureAppConfiguration)
            .Configure(Action<IApplicationBuilder> configureApp)
            .ConfigureServices(configureServices)
            .ConfigureLogging(configureLogging)
            .Build()
            .Run()
        0
    else
        let lambdaEntry = LambdaEntryPoint()
        let functionHandler = Func<APIGatewayProxyRequest, ILambdaContext, Task<APIGatewayProxyResponse>>(fun a b -> lambdaEntry.FunctionHandlerAsync(a, b))
        let handlerWrapper = HandlerWrapper.GetHandlerWrapper<APIGatewayProxyRequest, Task<APIGatewayProxyResponse>(functionHandler, JsonSerializer())
        let bootstrap = LambdaBootstrap(handlerWrapper)
        bootstrap.RunAsync().Wait()
        0