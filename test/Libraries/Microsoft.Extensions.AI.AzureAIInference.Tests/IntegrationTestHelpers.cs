﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Azure;
using Azure.AI.Inference;

namespace Microsoft.Extensions.AI;

/// <summary>Shared utility methods for integration tests.</summary>
internal static class IntegrationTestHelpers
{
    private static readonly string? _apiKey =
        Environment.GetEnvironmentVariable("AZURE_AI_INFERENCE_APIKEY") ??
        Environment.GetEnvironmentVariable("OPENAI_API_KEY");

    private static readonly string _endpoint =
        Environment.GetEnvironmentVariable("AZURE_AI_INFERENCE_ENDPOINT") ??
        "https://api.openai.com/v1";

    /// <summary>Gets an <see cref="ChatCompletionsClient"/> to use for testing, or null if the associated tests should be disabled.</summary>
    public static ChatCompletionsClient? GetChatCompletionsClient() =>
        _apiKey is string apiKey ?
            new ChatCompletionsClient(new Uri(_endpoint), new AzureKeyCredential(apiKey)) :
            null;

    /// <summary>Gets an <see cref="EmbeddingsClient"/> to use for testing, or null if the associated tests should be disabled.</summary>
    public static EmbeddingsClient? GetEmbeddingsClient() =>
        _apiKey is string apiKey ?
            new EmbeddingsClient(new Uri(_endpoint), new AzureKeyCredential(apiKey)) :
            null;
}