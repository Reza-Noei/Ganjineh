using PasswordManager.Domain;
using Xunit;

namespace PasswordManager.Domain.Tests;

public class ResultTests
{
    [Fact]
    public void Ok_Result_Is_Success_And_Exposes_Value()
    {
        var result = Result<int>.Ok(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Fail_Result_Carries_Error_And_Refuses_Value()
    {
        var result = Result<int>.Fail(AppError.AuthenticationFailed());

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.AuthenticationFailed, result.Error.Code);
        Assert.Throws<InvalidOperationException>(() => _ = result.Value);
    }

    [Fact]
    public void Non_Generic_Result_Supports_Error_Only_Flows()
    {
        var ok = Result.Ok();
        var failed = Result.Fail(AppError.Storage());

        Assert.True(ok.IsSuccess);
        Assert.False(failed.IsSuccess);
        Assert.Equal(ErrorCode.StorageError, failed.Error.Code);
    }

    [Fact]
    public void Fail_Rejects_Null_Error()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Fail(null!));
        Assert.Throws<ArgumentNullException>(() => Result<int>.Fail(null!));
    }

    [Fact]
    public void AuthenticationFailed_Uses_Stable_Message_Key()
    {
        // The key must stay stable: wrong-password and tampering are indistinguishable by design.
        Assert.Equal("Error.AuthenticationFailed", AppError.AuthenticationFailed().MessageKey);
    }
}
