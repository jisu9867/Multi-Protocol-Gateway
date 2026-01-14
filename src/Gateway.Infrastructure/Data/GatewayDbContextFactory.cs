using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Gateway.Infrastructure.Data;

public class GatewayDbContextFactory : IDesignTimeDbContextFactory<GatewayDbContext>
{
    public GatewayDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<GatewayDbContext>();
        
        // 1. 환경 변수에서 연결 문자열 읽기 (우선순위 1)
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        
        // 2. 환경 변수가 없으면 appsettings.json에서 읽기 (우선순위 2)
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Gateway.Api 프로젝트의 appsettings.json 찾기
            var basePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "Gateway.Api");
            if (!Directory.Exists(basePath))
            {
                // 다른 경로 시도: 현재 디렉토리에서 상대 경로로 찾기
                basePath = Path.Combine(Directory.GetCurrentDirectory(), "src", "Gateway.Api");
            }
            if (!Directory.Exists(basePath))
            {
                // 또 다른 경로: Infrastructure 프로젝트 기준
                basePath = Directory.GetCurrentDirectory();
            }
            
            var configuration = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .AddEnvironmentVariables()
                .Build();
            
            connectionString = configuration.GetConnectionString("DefaultConnection");
        }
        
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'DefaultConnection' not found. " +
                "Please set the 'ConnectionStrings__DefaultConnection' environment variable " +
                "or configure it in appsettings.json.");
        }
        
        optionsBuilder.UseNpgsql(connectionString);
        
        return new GatewayDbContext(optionsBuilder.Options);
    }
}