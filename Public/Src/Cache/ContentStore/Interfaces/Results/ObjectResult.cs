// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <nodoc />
    public static class ObjectResult
    {
        /// <nodoc />
        public static ObjectResult<T> Create<T>(T data) where T : class
            => new ObjectResult<T>(data);
    }

    /// <summary>
    ///     A boolean operation that returns an object.
    /// </summary>
    public class ObjectResult<T> : BoolResult, IEquatable<ObjectResult<T>>
        where T : class
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ObjectResult{T}"/> class.
        /// </summary>
        public ObjectResult()
            : base(errorMessage: "The operation was unsuccessful")
        {
            Data = default;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ObjectResult{T}"/> class.
        /// </summary>
        public ObjectResult(T obj)
        {
            Contract.RequiresNotNull(obj);
            Data = obj;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ObjectResult{T}"/> class.
        /// </summary>
        public ObjectResult(string errorMessage, string? diagnostics = null)
            : base(errorMessage, diagnostics)
        {
            Data = default;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ObjectResult{T}"/> class.
        /// </summary>
        public ObjectResult(Exception exception, string? message = null)
            : base(exception, message)
        {
            Data = default;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ObjectResult{T}"/> class.
        /// </summary>
        public ObjectResult(ResultBase other, string? message = null)
            : base(other, message)
        {
            Data = default;
        }

        /// <summary>
        /// Gets the result data.
        /// </summary>
        /// <remarks>
        /// If operation succeeded the returning value is not null.
        /// </remarks>
        public T? Data { get; }

        /// <inheritdoc />
        public bool Equals([AllowNull]ObjectResult<T> other)
        {
            if (other is null || !base.Equals(other))
            {
                return false;
            }

            if (ReferenceEquals(Data, other.Data))
            {
                return true;
            }

            if (Data == null || other.Data == null)
            {
                return false;
            }

            return Data.Equals(other.Data);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is ObjectResult<T> r && Equals(r);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return base.GetHashCode() ^ (Data?.GetHashCode() ?? 0);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Succeeded ? $"Success Data=[{Data}]" : GetErrorString();
        }
    }
}
