using Npgsql;
using Xunit;
namespace Paper.Api.Tests;
public sealed class PostgresConnectionTests
{
    [Fact] public void NeonUrlPreservesEscapedCredentialsAndTls()
    {
        var value=new NpgsqlConnectionStringBuilder(PostgresConnection.Parse("postgresql://owner:p%40ss%3Aword%2B42@ep-test-pooler.example/neondb?sslmode=require&channel_binding=require"));
        Assert.Equal("p@ss:word+42",value.Password);Assert.Equal("owner",value.Username);
        Assert.Equal("neondb",value.Database);Assert.Equal(5432,value.Port);
        Assert.Equal(SslMode.Require,value.SslMode);Assert.Equal(ChannelBinding.Require,value.ChannelBinding);
    }
    [Fact] public void NpgsqlSyntaxAndCustomPortRemainSupported()
    {
        var value=new NpgsqlConnectionStringBuilder(PostgresConnection.Parse("Host=localhost;Port=5433;Database=paper;Username=test;Password=test;SSL Mode=Disable"));
        Assert.Equal(5433,value.Port);Assert.Equal(SslMode.Disable,value.SslMode);
        Assert.Equal(10,value.MaxPoolSize);
    }
    [Theory][InlineData(null)][InlineData("")][InlineData("postgresql://owner:secret@example/neondb?unexpected=secret")]
    public void BadSettingsFailWithoutLeakingCredentials(string? value)
    {
        var error=Assert.Throws<InvalidOperationException>(()=>PostgresConnection.Parse(value));
        Assert.DoesNotContain("secret",error.ToString());Assert.Null(error.InnerException);
    }
}
