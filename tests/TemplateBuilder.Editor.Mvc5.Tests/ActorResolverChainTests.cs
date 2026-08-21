using System;
using System.Web;
using FluentAssertions;
using NSubstitute;
using TemplateBuilder.Editor.Mvc5;

namespace TemplateBuilder.Editor.Mvc5.Tests;

public class ActorResolverChainTests
{
    private static HttpContextBase Http() => Substitute.For<HttpContextBase>();

    [Fact]
    public void Resolve_uses_resolver_result_when_non_blank()
    {
        var resolver = new Func<HttpContextBase, string?>(_ => "jdoe");
        ActorResolverChain.Resolve(resolver, "alice", Http()).Should().Be("jdoe");
    }

    [Fact]
    public void Resolve_falls_back_to_identity_name_when_resolver_returns_null()
    {
        var resolver = new Func<HttpContextBase, string?>(_ => null);
        ActorResolverChain.Resolve(resolver, "alice", Http()).Should().Be("alice");
    }

    [Fact]
    public void Resolve_falls_back_to_identity_name_when_resolver_returns_whitespace()
    {
        var resolver = new Func<HttpContextBase, string?>(_ => "   ");
        ActorResolverChain.Resolve(resolver, "alice", Http()).Should().Be("alice");
    }

    [Fact]
    public void Resolve_falls_back_to_identity_name_when_no_resolver()
    {
        ActorResolverChain.Resolve(null, "alice", Http()).Should().Be("alice");
    }

    [Fact]
    public void Resolve_uses_anonymous_when_resolver_and_identity_are_absent()
    {
        ActorResolverChain.Resolve(null, null, Http()).Should().Be("anonymous");
    }

    [Fact]
    public void Resolve_uses_anonymous_when_identity_name_is_whitespace()
    {
        ActorResolverChain.Resolve(null, "  ", Http()).Should().Be("anonymous");
    }

    [Fact]
    public void Resolve_passes_http_context_to_resolver()
    {
        var ctx = Http();
        HttpContextBase? received = null;
        var resolver = new Func<HttpContextBase, string?>(c => { received = c; return "bob"; });
        ActorResolverChain.Resolve(resolver, "alice", ctx);
        received.Should().BeSameAs(ctx);
    }

    [Fact]
    public void Resolve_truncates_result_to_200_characters()
    {
        var longValue = new string('x', 250);
        ActorResolverChain.Resolve(null, longValue, Http()).Should().Be(new string('x', 200));
    }

    [Fact]
    public void Resolve_keeps_values_under_200_characters_unchanged()
    {
        ActorResolverChain.Resolve(null, "bob", Http()).Should().Be("bob");
    }

    [Fact]
    public void Resolve_propagates_resolver_exceptions()
    {
        var resolver = new Func<HttpContextBase, string?>(_ => throw new InvalidOperationException("user store down"));
        var act = () => ActorResolverChain.Resolve(resolver, "alice", Http());
        act.Should().Throw<InvalidOperationException>().WithMessage("user store down");
    }

    [Fact]
    public void Resolve_keeps_exactly_200_characters()
    {
        var value = new string('x', 200);
        ActorResolverChain.Resolve(null, value, Http()).Should().Be(value);
    }
}
