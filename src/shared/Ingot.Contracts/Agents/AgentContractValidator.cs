using System.Text.RegularExpressions;

namespace Ingot.Contracts.Agents;

public static partial class AgentContractValidator
{
    public static bool TryValidate(
        CreateChatRunRequest? request,
        out CreateChatRunRequest? normalized,
        out string error)
    {
        normalized = null;
        if (request is null)
        {
            error = "请求体不能为空。";
            return false;
        }
        if (!TryNormalize(
                request.Question,
                request.PageContext,
                request.Mode,
                out var question,
                out var pageContext,
                out var mode,
                out error))
            return false;

        normalized = new CreateChatRunRequest
        {
            Question = question!,
            Mode = mode!,
            PageContext = pageContext
        };
        return true;
    }

    private static bool TryNormalize(
        string? rawQuestion,
        PageContextRef? rawPageContext,
        string? rawMode,
        out string? question,
        out PageContextRef? pageContext,
        out string? mode,
        out string error)
    {
        question = null;
        pageContext = null;
        mode = null;
        if (rawQuestion is null)
        {
            error = "请求体不能为空。";
            return false;
        }

        question = rawQuestion.Trim();
        if (string.IsNullOrWhiteSpace(question) || question.Length > 4000)
        {
            error = "Question 长度必须在 1 到 4000 个字符之间。";
            return false;
        }

        mode = rawMode?.Trim().ToLowerInvariant();
        if (mode is not ("quick" or "combined"))
        {
            error = "Mode 只支持 standard 或 deep。";
            return false;
        }

        if (rawPageContext is not null)
        {
            var kind = rawPageContext.Kind?.Trim().ToLowerInvariant();
            var id = rawPageContext.Id?.Trim();
            if (string.IsNullOrWhiteSpace(kind) || !ContextKindRegex().IsMatch(kind) ||
                string.IsNullOrWhiteSpace(id) || id.Length > 200)
            {
                error = "PageContext 必须包含合法的 Kind 和 Id。";
                return false;
            }

            pageContext = new PageContextRef { Kind = kind, Id = id };
        }

        error = string.Empty;
        return true;
    }

    [GeneratedRegex("^[a-z][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ContextKindRegex();
}
