﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Net.Http;

namespace Microsoft.Extensions.Http.AutoClient;

/// <summary>
/// Defines an API <c>GET</c> method.
/// </summary>
/// <remarks>
/// Marks a method within an interface annotated with <see cref="AutoClientAttribute"/> as an API <c>GET</c> method.
///
/// The return type of an API method must be a <c>Task&lt;T&gt;</c>.
/// If T is a <see cref="string"/> and the dependency returns "text/plain" content type, the result will be the raw content of the response. Otherwise, it will be deserialized from JSON.
/// If T is of type <see cref="HttpResponseMessage"/>, the result will be the actual response message without further processing.
///
/// If you provide an extra parameter to the method, you should use it between curly braces in the URL to make it an URL parameter. For example: <c>/api/users/{userId}</c>.
/// </remarks>
/// <example>
/// <code>
/// [AutoClient("MyClient")]
/// interface IMyDependencyClient
/// {
///     [Get("/api/users/{userId}")]
///     Task&lt;User&gt; GetUserAsync(string userId, [Query] string filter, CancellationToken cancellationToken);
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method)]
public sealed class GetAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GetAttribute"/> class.
    /// </summary>
    /// <param name="path">The path of the request. Cannot be empty or null.</param>
    public GetAttribute(string path)
    {
        Path = path;
    }

    /// <summary>
    /// Gets the path of the request.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets or sets the name to use for this request within telemetry.
    /// </summary>
    /// <remarks>
    /// If this property is not provided, the request name is obtained from the method name.
    /// If the method name ends in 'Async', the request name will exclude that.
    /// For example, if the method is called <c>GetUsersAsync</c>, the request name, by default, will be <c>GetUsers</c>.
    /// </remarks>
    /// <example>
    /// <code>
    /// [AutoClient("MyClient")]
    /// interface IMyDependencyClient
    /// {
    ///     [Get("/api/users", RequestName = "ObtainUsers")]
    ///     Task&lt;string&gt; GetUsersAsync(CancellationToken cancellationToken = default);
    /// }
    /// </code>
    /// </example>
    public string? RequestName { get; set; }
}