using AgentHost.Llm.Prompts;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AgentHost.Tests;

public sealed class PromptTemplateLibraryTests
{
    [Fact]
    public void Render_UsesRegisteredTemplateVariables()
    {
        var library = new PromptTemplateLibrary(NullLogger<PromptTemplateLibrary>.Instance);
        library.RegisterTemplate("fhir-server-expert", "code-search", "Question: {{user_query}} | Context: {{repo_context}}");

        var rendered = library.Render("fhir-server-expert", "code-search", new Dictionary<string, string>
        {
            ["user_query"] = "Where is search implemented?",
            ["repo_context"] = "SearchParameterRegistry.cs"
        });

        rendered.Should().Be("Question: Where is search implemented? | Context: SearchParameterRegistry.cs");
    }

    [Fact]
    public void Render_MissingTemplate_FallsBackToUserQuery()
    {
        var library = new PromptTemplateLibrary(NullLogger<PromptTemplateLibrary>.Instance);

        var rendered = library.Render("missing-agent", "missing-skill", new Dictionary<string, string>
        {
            ["user_query"] = "fallback text"
        });

        rendered.Should().Be("fallback text");
    }
}
