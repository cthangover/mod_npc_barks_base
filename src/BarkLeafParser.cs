namespace Cthangover.Mods.NpcBarks
{
    using System;
    using System.Collections.Generic;
    using Cthangover.Core.Scenes;
    using Cthangover.Core.Scenes.Parsing;

    /// <summary>Adds spatial leaves to the shared condition grammar; role resolution stays in the call context.</summary>
    public sealed class BarkLeafParser : ILeafParser
    {
        public bool CanParse(TokenPeeker peek) => peek.Peek.Type == ConditionParser.TokenType.Ident
            && peek.Peek.Value is "near" or "visible" or "moving" or "can_hear"
            && peek.PeekAhead(1).Type == ConditionParser.TokenType.LParen;
        public ICondition Parse(TokenPeeker peek, Func<ICondition> parseSubExpr)
        {
            var function = peek.Advance().Value;
            peek.Expect(ConditionParser.TokenType.LParen, "expected (");
            var args = new List<string>();
            while (peek.Check(ConditionParser.TokenType.String) || peek.Check(ConditionParser.TokenType.Ident) || peek.Check(ConditionParser.TokenType.Number))
            {
                args.Add(peek.Advance().Value);
                if (!peek.Check(ConditionParser.TokenType.Comma)) break;
                peek.Advance();
            }
            peek.Expect(ConditionParser.TokenType.RParen, "expected )");
            var count = function == "near" ? 3 : function == "can_hear" ? 2 : 1;
            if (args.Count != count) throw new FormatException(function + " expects " + count + " arguments");
            if (function == "near" && (!int.TryParse(args[2], out var radius) || radius < 0 || radius > 1000)) throw new FormatException("near radius must be an integer 0..1000");
            return new BarkCondition(function, args.ToArray());
        }
    }
}
