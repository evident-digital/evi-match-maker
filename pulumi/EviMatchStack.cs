using Pulumi;
using Pulumi.Azure.AppInsights;
using Pulumi.Azure.AppService;
using Pulumi.Azure.AppService.Inputs;
using Pulumi.Azure.Core;
using Pulumi.Azure.Sql;

class EviMatchStack : Stack
{
    private Config config = new Config();
    [Output] Output<string> BackendHostname { get; set; }
    [Output] Output<string> FrontendHostname { get; set; }

    public EviMatchStack()
    {
        //var resourceGroup = new ResourceGroup(CorrectNaming("rg"));
        var resourceGroup  = ResourceGroup.Get("rg-we-evimatch-" + config.Get("environment"), config.Get("resourcegroupid"));

        var beApp = DeployBackend(resourceGroup);

        var feApp = DeployFrontend(resourceGroup);
        
        this.BackendHostname = beApp.DefaultSiteHostname;
        this.FrontendHostname = feApp.DefaultSiteHostname;
    }

    private AppService DeployBackend(ResourceGroup resourceGroup)
    {
        var appServicePlan = new Plan(CorrectNaming("asp-be"), new PlanArgs
        {
            ResourceGroupName = resourceGroup.Name,
            Kind = "App",
            Sku = new PlanSkuArgs
            {
                Tier = "Basic",
                Size = "B1",
            },
        });        

        var appInsights = new Insights(CorrectNaming("ai-be"), new InsightsArgs
        {
            ApplicationType = "web",
            ResourceGroupName = resourceGroup.Name
        });

        var username = config.Get("sqlAdmin") ?? "pulumi";
        var password = config.RequireSecret("sqlPassword");
        var sqlServer = new SqlServer(CorrectNaming("sqls"), new SqlServerArgs
        {
            ResourceGroupName = resourceGroup.Name,
            AdministratorLogin = username,
            AdministratorLoginPassword = password,
            Version = "12.0",
        });

        var database = new Database("sql", new DatabaseArgs
        {
            ResourceGroupName = resourceGroup.Name,
            ServerName = sqlServer.Name,
            RequestedServiceObjectiveName = "S0",
        });

        var app = new AppService(CorrectNaming("as-be"), new AppServiceArgs
        {
            ResourceGroupName = resourceGroup.Name,
            AppServicePlanId = appServicePlan.Id,
            AppSettings =
            {
                {"APPINSIGHTS_INSTRUMENTATIONKEY", appInsights.InstrumentationKey},
                {"APPLICATIONINSIGHTS_CONNECTION_STRING", appInsights.InstrumentationKey.Apply(key => $"InstrumentationKey={key}")},
                {"ApplicationInsightsAgent_EXTENSION_VERSION", "~2"},
            },
            ConnectionStrings =
            {
                new AppServiceConnectionStringArgs
                {
                    Name = "db",
                    Type = "SQLAzure",
                    Value = Output.Tuple<string, string, string>(sqlServer.Name, database.Name, password).Apply(t =>
                    {
                        (string server, string database, string pwd) = t;
                        return
                            $"Server= tcp:{server}.database.windows.net;initial catalog={database};userID={username};password={pwd};Min Pool Size=0;Max Pool Size=30;Persist Security Info=true;";
                    }),
                },
            },
        });

        return app;
    }
    private AppService DeployFrontend(ResourceGroup resourceGroup)
    {
        var appServicePlan = new Plan(CorrectNaming("asp-fe"), new PlanArgs
        {
            ResourceGroupName = resourceGroup.Name,
            Kind = "App",
            Sku = new PlanSkuArgs
            {
                Tier = "Basic",
                Size = "B1",
            },
        });
        
        var appInsights = new Insights(CorrectNaming("ai-fe"), new InsightsArgs
        {
            ApplicationType = "web",
            ResourceGroupName = resourceGroup.Name
        });

        var app = new AppService(CorrectNaming("as-fe"), new AppServiceArgs
        {
            ResourceGroupName = resourceGroup.Name,
            AppServicePlanId = appServicePlan.Id,
            AppSettings =
            {
                {"APPINSIGHTS_INSTRUMENTATIONKEY", appInsights.InstrumentationKey},
                {"APPLICATIONINSIGHTS_CONNECTION_STRING", appInsights.InstrumentationKey.Apply(key => $"InstrumentationKey={key}")},
                {"ApplicationInsightsAgent_EXTENSION_VERSION", "~2"},
            }
        });

        return app;
    }

    private string CorrectNaming(string toConfigure)
    {
        return string.Concat(toConfigure, "-", config.Get("location"), "-", config.Get("environment"));
    }
}