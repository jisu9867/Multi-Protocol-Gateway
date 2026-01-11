using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gateway.Infrastructure.Data;

public class GatewayDbContextFactory : IDesignTimeDbContextFactory<GatewayDbContext>
{
    public GatewayDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<GatewayDbContext>();
        
        // 환경 변수에서 연결 문자열 읽기 (ConnectionStrings__DefaultConnection)
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'DefaultConnection' not found. " +
                "Please set the 'ConnectionStrings__DefaultConnection' environment variable.");
        }
        
        optionsBuilder.UseNpgsql(connectionString);
        
        return new GatewayDbContext(optionsBuilder.Options);
    }
}