using System.Diagnostics.CodeAnalysis;

using k8s;
using k8s.Models;

namespace KubeOps.Abstractions.Controller;

public sealed record Result<TEntity>
    where TEntity : IKubernetesObject<V1ObjectMeta>
{
    private Result(TEntity entity, bool isSuccess, string? errorMessage, Exception? error, TimeSpan? requeueAfter)
    {
        Entity = entity;
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        Error = error;
        RequeueAfter = requeueAfter;
    }

    public TEntity Entity { get; }

    [MemberNotNullWhen(false, nameof(ErrorMessage))]
    public bool IsSuccess { get; }

    [MemberNotNullWhen(true, nameof(ErrorMessage))]
    public bool IsFailure => !IsSuccess;

    public string? ErrorMessage { get; set; }

    public Exception? Error { get; }

    public TimeSpan? RequeueAfter { get; }

    public static Result<TEntity> ForSuccess(TEntity entity, TimeSpan? requeueAfter = null)
    {
        return new(entity, true, null, null, requeueAfter);
    }

    public static Result<TEntity> ForFailure(TEntity entity, string errorMessage, Exception? error = null, TimeSpan? requeueAfter = null)
    {
        return new(entity, false, errorMessage, error, requeueAfter);
    }
}
