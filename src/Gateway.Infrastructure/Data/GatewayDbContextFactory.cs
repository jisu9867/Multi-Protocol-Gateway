using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gateway.Infrastructure.Data;

public class GatewayDbContextFactory : IDesignTimeDbContextFactory<GatewayDbContext>
{
    public GatewayDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<GatewayDbContext>();
        
        // --connection으로 전달된 연결 문자열은 EF Core가 자동으로 사용
        // 여기서는 더미 연결 문자열만 제공 (실제로는 사용되지 않음)
        optionsBuilder.UseNpgsql("");
        
        return new GatewayDbContext(optionsBuilder.Options);
    }
}