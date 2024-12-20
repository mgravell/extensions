﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma warning disable SA1204 // Static elements should appear before instance elements

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using OpenAI.Chat;

namespace Microsoft.Extensions.AI;

internal static partial class OpenAIModelMappers
{
    public static OpenAIChatCompletionRequest FromOpenAIChatCompletionRequest(OpenAI.Chat.ChatCompletionOptions chatCompletionOptions)
    {
        ChatOptions chatOptions = FromOpenAIOptions(chatCompletionOptions);
        IList<ChatMessage> messages = FromOpenAIChatMessages(_getMessagesAccessor(chatCompletionOptions)).ToList();
        return new()
        {
            Messages = messages,
            Options = chatOptions,
            ModelId = chatOptions.ModelId,
            Stream = _getStreamAccessor(chatCompletionOptions) ?? false,
        };
    }

    public static IEnumerable<ChatMessage> FromOpenAIChatMessages(IEnumerable<OpenAI.Chat.ChatMessage> inputs)
    {
        // Maps all of the OpenAI types to the corresponding M.E.AI types.
        // Unrecognized or non-processable content is ignored.

        Dictionary<string, string>? functionCalls = null;

        foreach (OpenAI.Chat.ChatMessage input in inputs)
        {
            switch (input)
            {
                case SystemChatMessage systemMessage:
                    yield return new ChatMessage
                    {
                        Role = ChatRole.System,
                        AuthorName = systemMessage.ParticipantName,
                        Contents = FromOpenAIChatContent(systemMessage.Content),
                    };
                    break;

                case UserChatMessage userMessage:
                    yield return new ChatMessage
                    {
                        Role = ChatRole.User,
                        AuthorName = userMessage.ParticipantName,
                        Contents = FromOpenAIChatContent(userMessage.Content),
                    };
                    break;

                case ToolChatMessage toolMessage:
                    string textContent = string.Join(string.Empty, toolMessage.Content.Where(part => part.Kind is ChatMessageContentPartKind.Text).Select(part => part.Text));
                    object? result = textContent;
                    if (!string.IsNullOrEmpty(textContent))
                    {
#pragma warning disable CA1031 // Do not catch general exception types
                        try
                        {
                            result = JsonSerializer.Deserialize(textContent, AIJsonUtilities.DefaultOptions.GetTypeInfo(typeof(object)));
                        }
                        catch
                        {
                            // If the content can't be deserialized, leave it as a string.
                        }
#pragma warning restore CA1031 // Do not catch general exception types
                    }

                    string functionName = functionCalls?.TryGetValue(toolMessage.ToolCallId, out string? name) is true ? name : string.Empty;
                    yield return new ChatMessage
                    {
                        Role = ChatRole.Tool,
                        Contents = new AIContent[] { new FunctionResultContent(toolMessage.ToolCallId, functionName, result) },
                    };
                    break;

                case AssistantChatMessage assistantMessage:

                    ChatMessage message = new()
                    {
                        Role = ChatRole.Assistant,
                        AuthorName = assistantMessage.ParticipantName,
                        Contents = FromOpenAIChatContent(assistantMessage.Content),
                    };

                    foreach (ChatToolCall toolCall in assistantMessage.ToolCalls)
                    {
                        if (!string.IsNullOrWhiteSpace(toolCall.FunctionName))
                        {
                            var callContent = ParseCallContentFromBinaryData(toolCall.FunctionArguments, toolCall.Id, toolCall.FunctionName);
                            callContent.RawRepresentation = toolCall;

                            message.Contents.Add(callContent);
                            (functionCalls ??= new()).Add(toolCall.Id, toolCall.FunctionName);
                        }
                    }

                    if (assistantMessage.Refusal is not null)
                    {
                        message.AdditionalProperties ??= [];
                        message.AdditionalProperties.Add(nameof(assistantMessage.Refusal), assistantMessage.Refusal);
                    }

                    yield return message;
                    break;
            }
        }
    }

    /// <summary>Converts an Extensions chat message enumerable to an OpenAI chat message enumerable.</summary>
    public static IEnumerable<OpenAI.Chat.ChatMessage> ToOpenAIChatMessages(IEnumerable<ChatMessage> inputs, JsonSerializerOptions options)
    {
        // Maps all of the M.E.AI types to the corresponding OpenAI types.
        // Unrecognized or non-processable content is ignored.

        foreach (ChatMessage input in inputs)
        {
            if (input.Role == ChatRole.System || input.Role == ChatRole.User)
            {
                var parts = ToOpenAIChatContent(input.Contents);
                yield return input.Role == ChatRole.System ?
                    new SystemChatMessage(parts) { ParticipantName = input.AuthorName } :
                    new UserChatMessage(parts) { ParticipantName = input.AuthorName };
            }
            else if (input.Role == ChatRole.Tool)
            {
                foreach (AIContent item in input.Contents)
                {
                    if (item is FunctionResultContent resultContent)
                    {
                        string? result = resultContent.Result as string;
                        if (result is null && resultContent.Result is not null)
                        {
                            try
                            {
                                result = JsonSerializer.Serialize(resultContent.Result, options.GetTypeInfo(typeof(object)));
                            }
                            catch (NotSupportedException)
                            {
                                // If the type can't be serialized, skip it.
                            }
                        }

                        yield return new ToolChatMessage(resultContent.CallId, result ?? string.Empty);
                    }
                }
            }
            else if (input.Role == ChatRole.Assistant)
            {
                AssistantChatMessage message = new(ToOpenAIChatContent(input.Contents))
                {
                    ParticipantName = input.AuthorName
                };

                foreach (var content in input.Contents)
                {
                    if (content is FunctionCallContent callRequest)
                    {
                        message.ToolCalls.Add(
                            ChatToolCall.CreateFunctionToolCall(
                                callRequest.CallId,
                                callRequest.Name,
                                new(JsonSerializer.SerializeToUtf8Bytes(
                                    callRequest.Arguments,
                                    options.GetTypeInfo(typeof(IDictionary<string, object?>))))));
                    }
                }

                if (input.AdditionalProperties?.TryGetValue(nameof(message.Refusal), out string? refusal) is true)
                {
                    message.Refusal = refusal;
                }

                yield return message;
            }
        }
    }

    private static List<AIContent> FromOpenAIChatContent(IList<ChatMessageContentPart> openAiMessageContentParts)
    {
        List<AIContent> contents = new();
        foreach (var openAiContentPart in openAiMessageContentParts)
        {
            switch (openAiContentPart.Kind)
            {
                case ChatMessageContentPartKind.Text:
                    contents.Add(new TextContent(openAiContentPart.Text));
                    break;

                case ChatMessageContentPartKind.Image when (openAiContentPart.ImageBytes is { } bytes):
                    contents.Add(new ImageContent(bytes.ToArray(), openAiContentPart.ImageBytesMediaType));
                    break;

                case ChatMessageContentPartKind.Image:
                    contents.Add(new ImageContent(openAiContentPart.ImageUri?.ToString() ?? string.Empty));
                    break;

            }
        }

        return contents;
    }

    /// <summary>Converts a list of <see cref="AIContent"/> to a list of <see cref="ChatMessageContentPart"/>.</summary>
    private static List<ChatMessageContentPart> ToOpenAIChatContent(IList<AIContent> contents)
    {
        List<ChatMessageContentPart> parts = [];
        foreach (var content in contents)
        {
            switch (content)
            {
                case TextContent textContent:
                    parts.Add(ChatMessageContentPart.CreateTextPart(textContent.Text));
                    break;

                case ImageContent imageContent when imageContent.Data is { IsEmpty: false } data:
                    parts.Add(ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(data), imageContent.MediaType));
                    break;

                case ImageContent imageContent when imageContent.Uri is string uri:
                    parts.Add(ChatMessageContentPart.CreateImagePart(new Uri(uri)));
                    break;
            }
        }

        if (parts.Count == 0)
        {
            parts.Add(ChatMessageContentPart.CreateTextPart(string.Empty));
        }

        return parts;
    }

#pragma warning disable S3011 // Reflection should not be used to increase accessibility of classes, methods, or fields
    private static readonly Func<ChatCompletionOptions, IList<OpenAI.Chat.ChatMessage>> _getMessagesAccessor =
        (Func<ChatCompletionOptions, IList<OpenAI.Chat.ChatMessage>>)
            typeof(ChatCompletionOptions).GetMethod("get_Messages", BindingFlags.NonPublic | BindingFlags.Instance)!
            .CreateDelegate(typeof(Func<ChatCompletionOptions, IList<OpenAI.Chat.ChatMessage>>))!;

    private static readonly Func<ChatCompletionOptions, bool?> _getStreamAccessor =
        (Func<ChatCompletionOptions, bool?>)
            typeof(ChatCompletionOptions).GetMethod("get_Stream", BindingFlags.NonPublic | BindingFlags.Instance)!
            .CreateDelegate(typeof(Func<ChatCompletionOptions, bool?>))!;

    private static readonly MethodInfo _getModelIdAccessor =
        typeof(ChatCompletionOptions).GetMethod("get_Model", BindingFlags.NonPublic | BindingFlags.Instance)!;
#pragma warning restore S3011 // Reflection should not be used to increase accessibility of classes, methods, or fields
}