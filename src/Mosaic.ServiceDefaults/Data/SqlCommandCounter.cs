using System.Data.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Mosaic.ServiceDefaults.Diagnostics;

namespace Mosaic.ServiceDefaults.Data;

/// <summary>
/// Counts the commands Entity Framework Core actually sends to PostgreSQL
/// while one GraphQL request is being served.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately measured at the lowest point available rather than
/// inferred higher up. A resolver that calls a service that calls a DataLoader
/// that shares a context is several layers away from a round trip; an
/// interceptor on the command is not.
/// </para>
/// <para>
/// The interceptor is registered on the pooled factory's options, so there is
/// one instance for the whole process and it cannot hold per-request state.
/// It finds the request's timeline the same way chapter 3's listener does,
/// through <c>RequestServices</c>, reached here through the
/// <see cref="IHttpContextAccessor"/>. That accessor stores the context in an
/// <c>AsyncLocal</c>, which flows into the tasks a DataLoader batch is
/// dispatched on because HotChocolate's batch dispatcher is registered per
/// request scope and its coordinator loop starts inside the request.
/// </para>
/// </remarks>
public sealed class SqlCommandCounter(IHttpContextAccessor httpContextAccessor)
    : DbCommandInterceptor
{
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Count();
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Count();
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        Count();
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Count();
        return ValueTask.FromResult(result);
    }

    private void Count()
        => httpContextAccessor.HttpContext?.RequestServices
            .GetService<RequestTimeline>()
            ?.CountSqlCommand();
}
