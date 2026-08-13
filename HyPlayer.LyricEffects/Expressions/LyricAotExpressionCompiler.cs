using System.Linq.Expressions;

namespace HyPlayer.LyricEffects.Expressions;

/// <summary>
/// Builds allocation-free evaluators for the expression shapes accepted by the lyric DSL.
/// Unsupported shapes deliberately fall back to the LINQ expression interpreter.
/// </summary>
internal static class LyricAotExpressionCompiler
{
    private delegate Value Node(in EvaluationContext context);

    public static bool TryCompile<TDelegate>(Expression<TDelegate> expression, out TDelegate compiled)
        where TDelegate : Delegate
    {
        try
        {
            var node = CompileNode(expression.Body);
            Delegate result;
            if (typeof(TDelegate) == typeof(LyricScalarExpression))
            {
                result = (LyricScalarExpression)((line, frame, fx) =>
                {
                    var context = EvaluationContext.WholeLine(line, frame, fx);
                    return node(in context).AsFloat();
                });
            }
            else if (typeof(TDelegate) == typeof(LyricColorExpression))
            {
                result = (LyricColorExpression)((line, frame, fx) =>
                {
                    var context = EvaluationContext.WholeLine(line, frame, fx);
                    return node(in context).AsColor();
                });
            }
            else if (typeof(TDelegate) == typeof(LyricTextExpression))
            {
                result = (LyricTextExpression)((line, frame, fx) =>
                {
                    var context = EvaluationContext.WholeLine(line, frame, fx);
                    return node(in context).AsText() ?? string.Empty;
                });
            }
            else if (typeof(TDelegate) == typeof(FocusedTextScalarExpression))
            {
                result = (FocusedTextScalarExpression)((line, frame, text, word, glyph, fx) =>
                {
                    var context = EvaluationContext.Focused(line, frame, text, word, glyph, fx);
                    return node(in context).AsFloat();
                });
            }
            else if (typeof(TDelegate) == typeof(FocusedTextColorExpression))
            {
                result = (FocusedTextColorExpression)((line, frame, text, word, glyph, fx) =>
                {
                    var context = EvaluationContext.Focused(line, frame, text, word, glyph, fx);
                    return node(in context).AsColor();
                });
            }
            else if (typeof(TDelegate) == typeof(FocusedTextTextExpression))
            {
                result = (FocusedTextTextExpression)((line, frame, text, word, glyph, fx) =>
                {
                    var context = EvaluationContext.Focused(line, frame, text, word, glyph, fx);
                    return node(in context).AsText() ?? string.Empty;
                });
            }
            else
            {
                compiled = null!;
                return false;
            }

            compiled = (TDelegate)result;
            return true;
        }
        catch (NotSupportedException)
        {
            compiled = null!;
            return false;
        }
    }

    private static Node CompileNode(Expression expression)
    {
        return expression switch
        {
            ConstantExpression constant => CompileConstant(constant),
            MemberExpression member => CompileMember(member),
            ConditionalExpression conditional => CompileConditional(conditional),
            BinaryExpression binary => CompileBinary(binary),
            UnaryExpression unary => CompileUnary(unary),
            MethodCallExpression call => CompileCall(call),
            _ => throw Unsupported(expression)
        };
    }

    private static Node CompileConstant(ConstantExpression expression)
    {
        var value = Value.FromConstant(expression.Value);
        return (in EvaluationContext _) => value;
    }

    private static Node CompileMember(MemberExpression expression)
    {
        if (TryGetRootPath(expression, out var root, out var path))
        {
            try
            {
                return CompileRootMember(root, path);
            }
            catch (NotSupportedException) when (expression.Expression is MemberExpression)
            {
                // A supported root value can still have another supported member, e.g. line.Text.Length.
            }
        }

        if (expression.Expression is null) throw Unsupported(expression);
        var target = CompileNode(expression.Expression);
        return expression.Member.Name switch
        {
            "A" when expression.Expression.Type == typeof(LyricColorValue) =>
                (in EvaluationContext context) => Value.FromInt64(target(in context).AsColor().A),
            "R" when expression.Expression.Type == typeof(LyricColorValue) =>
                (in EvaluationContext context) => Value.FromInt64(target(in context).AsColor().R),
            "G" when expression.Expression.Type == typeof(LyricColorValue) =>
                (in EvaluationContext context) => Value.FromInt64(target(in context).AsColor().G),
            "B" when expression.Expression.Type == typeof(LyricColorValue) =>
                (in EvaluationContext context) => Value.FromInt64(target(in context).AsColor().B),
            "Length" when expression.Expression.Type == typeof(string) =>
                (in EvaluationContext context) => Value.FromInt64(target(in context).AsText()?.Length ?? 0),
            _ => throw Unsupported(expression)
        };
    }

    private static Node CompileConditional(ConditionalExpression expression)
    {
        var test = CompileNode(expression.Test);
        var whenTrue = CompileNode(expression.IfTrue);
        var whenFalse = CompileNode(expression.IfFalse);
        return (in EvaluationContext context) => test(in context).AsBoolean()
            ? whenTrue(in context)
            : whenFalse(in context);
    }

    private static Node CompileBinary(BinaryExpression expression)
    {
        var left = CompileNode(expression.Left);
        var right = CompileNode(expression.Right);
        if (expression.NodeType == ExpressionType.AndAlso)
            return (in EvaluationContext context) =>
                Value.FromBoolean(left(in context).AsBoolean() && right(in context).AsBoolean());
        if (expression.NodeType == ExpressionType.OrElse)
            return (in EvaluationContext context) =>
                Value.FromBoolean(left(in context).AsBoolean() || right(in context).AsBoolean());
        if (expression.NodeType == ExpressionType.Coalesce)
            return (in EvaluationContext context) =>
            {
                var value = left(in context);
                return value.Kind == ValueKind.Null ? right(in context) : value;
            };

        if (expression.NodeType is not (
                ExpressionType.Add or ExpressionType.Subtract or ExpressionType.Multiply or
                ExpressionType.Divide or ExpressionType.Modulo or ExpressionType.Equal or
                ExpressionType.NotEqual or ExpressionType.LessThan or ExpressionType.LessThanOrEqual or
                ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual or ExpressionType.And or
                ExpressionType.Or or ExpressionType.ExclusiveOr or ExpressionType.LeftShift or
                ExpressionType.RightShift))
            throw Unsupported(expression);

        return (in EvaluationContext context) => EvaluateBinary(
            expression.NodeType,
            expression.Left.Type,
            expression.Right.Type,
            expression.Type,
            left(in context),
            right(in context));
    }

    private static Node CompileUnary(UnaryExpression expression)
    {
        var operand = CompileNode(expression.Operand);
        return expression.NodeType switch
        {
            ExpressionType.Convert =>
                (in EvaluationContext context) => Convert(operand(in context), expression.Type),
            ExpressionType.Negate =>
                (in EvaluationContext context) => Negate(operand(in context), expression.Type),
            ExpressionType.UnaryPlus => operand,
            ExpressionType.Not when expression.Type == typeof(bool) =>
                (in EvaluationContext context) => Value.FromBoolean(!operand(in context).AsBoolean()),
            ExpressionType.Not => (in EvaluationContext context) =>
                Value.FromInt64(~operand(in context).AsInt64()),
            _ => throw Unsupported(expression)
        };
    }

    private static Node CompileCall(MethodCallExpression expression)
    {
        var arguments = new Node[expression.Arguments.Count];
        for (var index = 0; index < arguments.Length; index++)
            arguments[index] = CompileNode(expression.Arguments[index]);

        if (expression.Method.DeclaringType == typeof(string) && expression.Method.Name == nameof(string.Concat))
            return CompileStringConcat(expression.Arguments, arguments);

        if (expression.Method.DeclaringType != typeof(LyricExpressionFunctions) &&
            !(expression.Method.Name == "Rgba" && expression.Method.ReturnType == typeof(LyricColorValue)))
            throw Unsupported(expression);

        return expression.Method.Name switch
        {
            nameof(LyricExpressionFunctions.Min) => Numeric2(arguments, MathF.Min),
            nameof(LyricExpressionFunctions.Max) => Numeric2(arguments, MathF.Max),
            nameof(LyricExpressionFunctions.Clamp) => Numeric3(arguments, Math.Clamp),
            nameof(LyricExpressionFunctions.Lerp) => Numeric3(arguments, static (a, b, t) => a + (b - a) * t),
            nameof(LyricExpressionFunctions.SmoothStep) => Numeric3(arguments, static (a, b, t) =>
            {
                t = Math.Clamp(t, 0, 1);
                t = t * t * (3 - 2 * t);
                return a + (b - a) * t;
            }),
            nameof(LyricExpressionFunctions.Abs) => Numeric1(arguments, MathF.Abs),
            nameof(LyricExpressionFunctions.Sin) => Numeric1(arguments, MathF.Sin),
            nameof(LyricExpressionFunctions.Cos) => Numeric1(arguments, MathF.Cos),
            nameof(LyricExpressionFunctions.Pow) => Numeric2(arguments, MathF.Pow),
            nameof(LyricExpressionFunctions.Color) => (in EvaluationContext context) =>
                Value.FromColor(context.Functions.Color(arguments[0](in context).AsText() ?? string.Empty)),
            nameof(LyricExpressionFunctions.Rgba) => (in EvaluationContext context) =>
                Value.FromColor(context.Functions.Rgba(
                    arguments[0](in context).AsFloat(),
                    arguments[1](in context).AsFloat(),
                    arguments[2](in context).AsFloat(),
                    arguments[3](in context).AsFloat())),
            nameof(LyricExpressionFunctions.LerpColor) => (in EvaluationContext context) =>
                Value.FromColor(context.Functions.LerpColor(
                    arguments[0](in context).AsColor(),
                    arguments[1](in context).AsColor(),
                    arguments[2](in context).AsFloat())),
            _ => throw Unsupported(expression)
        };
    }

    private static Node Numeric1(Node[] arguments, Func<float, float> function)
    {
        if (arguments.Length != 1) throw new NotSupportedException();
        return (in EvaluationContext context) => Value.FromFloat(function(arguments[0](in context).AsFloat()));
    }

    private static Node Numeric2(Node[] arguments, Func<float, float, float> function)
    {
        if (arguments.Length != 2) throw new NotSupportedException();
        return (in EvaluationContext context) => Value.FromFloat(function(
            arguments[0](in context).AsFloat(), arguments[1](in context).AsFloat()));
    }

    private static Node Numeric3(Node[] arguments, Func<float, float, float, float> function)
    {
        if (arguments.Length != 3) throw new NotSupportedException();
        return (in EvaluationContext context) => Value.FromFloat(function(
            arguments[0](in context).AsFloat(),
            arguments[1](in context).AsFloat(),
            arguments[2](in context).AsFloat()));
    }

    private static Node CompileStringConcat(IReadOnlyList<Expression> expressions, Node[] arguments)
    {
        for (var index = 0; index < expressions.Count; index++)
            if (expressions[index].Type != typeof(string)) throw new NotSupportedException();

        return arguments.Length switch
        {
            2 => (in EvaluationContext context) => Value.FromText(string.Concat(
                arguments[0](in context).AsText(), arguments[1](in context).AsText())),
            3 => (in EvaluationContext context) => Value.FromText(string.Concat(
                arguments[0](in context).AsText(), arguments[1](in context).AsText(),
                arguments[2](in context).AsText())),
            4 => (in EvaluationContext context) => Value.FromText(string.Concat(
                arguments[0](in context).AsText(), arguments[1](in context).AsText(),
                arguments[2](in context).AsText(), arguments[3](in context).AsText())),
            _ => throw new NotSupportedException()
        };
    }

    private static Value EvaluateBinary(
        ExpressionType operation,
        Type leftType,
        Type rightType,
        Type resultType,
        Value left,
        Value right)
    {
        if (operation is ExpressionType.Equal or ExpressionType.NotEqual)
        {
            var equal = left.Kind == ValueKind.Null || right.Kind == ValueKind.Null
                ? left.Kind == right.Kind
                : leftType == typeof(string) || rightType == typeof(string)
                    ? string.Equals(left.AsText(), right.AsText(), StringComparison.Ordinal)
                    : leftType == typeof(LyricColorValue) || rightType == typeof(LyricColorValue)
                        ? left.AsColor() == right.AsColor()
                        : leftType == typeof(bool) && rightType == typeof(bool)
                            ? left.AsBoolean() == right.AsBoolean()
                            : NumericEquals(leftType, rightType, left, right);
            return Value.FromBoolean(operation == ExpressionType.Equal ? equal : !equal);
        }

        if (operation is ExpressionType.LessThan or ExpressionType.LessThanOrEqual or
            ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual)
        {
            var comparison = IsFloating(leftType) || IsFloating(rightType)
                ? left.AsFloat().CompareTo(right.AsFloat())
                : left.AsInt64().CompareTo(right.AsInt64());
            return Value.FromBoolean(operation switch
            {
                ExpressionType.LessThan => comparison < 0,
                ExpressionType.LessThanOrEqual => comparison <= 0,
                ExpressionType.GreaterThan => comparison > 0,
                _ => comparison >= 0
            });
        }

        if (resultType == typeof(string) && operation == ExpressionType.Add)
            return Value.FromText(string.Concat(left.AsText(), right.AsText()));
        if (resultType == typeof(bool) && operation is ExpressionType.And or ExpressionType.Or or ExpressionType.ExclusiveOr)
        {
            var a = left.AsBoolean();
            var b = right.AsBoolean();
            return Value.FromBoolean(operation switch
            {
                ExpressionType.And => a & b,
                ExpressionType.Or => a | b,
                _ => a ^ b
            });
        }
        if (resultType == typeof(float) || resultType == typeof(double))
        {
            var a = left.AsFloat();
            var b = right.AsFloat();
            return Value.FromFloat(operation switch
            {
                ExpressionType.Add => a + b,
                ExpressionType.Subtract => a - b,
                ExpressionType.Multiply => a * b,
                ExpressionType.Divide => a / b,
                ExpressionType.Modulo => a % b,
                _ => throw new NotSupportedException()
            });
        }

        var integerLeft = left.AsInt64();
        var integerRight = right.AsInt64();
        var result = operation switch
        {
            ExpressionType.Add => integerLeft + integerRight,
            ExpressionType.Subtract => integerLeft - integerRight,
            ExpressionType.Multiply => integerLeft * integerRight,
            ExpressionType.Divide => integerLeft / integerRight,
            ExpressionType.Modulo => integerLeft % integerRight,
            ExpressionType.And => integerLeft & integerRight,
            ExpressionType.Or => integerLeft | integerRight,
            ExpressionType.ExclusiveOr => integerLeft ^ integerRight,
            ExpressionType.LeftShift => integerLeft << (int)integerRight,
            ExpressionType.RightShift => integerLeft >> (int)integerRight,
            _ => throw new NotSupportedException()
        };
        return Value.FromInt64(resultType == typeof(int) ? (int)result : result);
    }

    private static bool NumericEquals(Type leftType, Type rightType, Value left, Value right) =>
        IsFloating(leftType) || IsFloating(rightType)
            ? left.AsFloat() == right.AsFloat()
            : left.AsInt64() == right.AsInt64();

    private static Value Convert(Value value, Type targetType)
    {
        if (targetType == typeof(float) || targetType == typeof(double)) return Value.FromFloat(value.AsFloat());
        if (targetType == typeof(int) || targetType == typeof(long) || targetType == typeof(byte))
            return Value.FromInt64(value.AsInt64());
        if (targetType == typeof(bool)) return Value.FromBoolean(value.AsBoolean());
        if (targetType == typeof(string)) return Value.FromText(value.AsText());
        if (targetType == typeof(LyricColorValue)) return Value.FromColor(value.AsColor());
        if (targetType == typeof(object)) return value;
        throw new NotSupportedException();
    }

    private static Value Negate(Value value, Type type) => IsFloating(type)
        ? Value.FromFloat(-value.AsFloat())
        : Value.FromInt64(-value.AsInt64());

    private static bool IsFloating(Type type) => type == typeof(float) || type == typeof(double);

    private static bool TryGetRootPath(MemberExpression expression, out string root, out string path)
    {
        var members = new List<string>();
        Expression? current = expression;
        while (current is MemberExpression member)
        {
            members.Add(member.Member.Name);
            current = member.Expression;
        }

        if (current is not ParameterExpression parameter)
        {
            root = path = string.Empty;
            return false;
        }

        members.Reverse();
        root = parameter.Name ?? string.Empty;
        path = string.Join('.', members);
        return true;
    }

    private static Node CompileRootMember(string root, string path)
    {
        return root switch
        {
            "line" => CompileLineMember(path),
            "frame" => CompileFrameMember(path),
            "text" => CompileTextMember(path),
            "word" => CompileWordMember(path),
            "glyph" => CompileGlyphMember(path),
            _ => throw new NotSupportedException()
        };
    }

    private static Node CompileLineMember(string path) => path switch
    {
        "Index" => (in EvaluationContext c) => Value.FromInt64(c.Line.Index),
        "RelativeIndex" => (in EvaluationContext c) => Value.FromInt64(c.Line.RelativeIndex),
        "IndexDistance" => (in EvaluationContext c) => Value.FromFloat(c.Line.IndexDistance),
        "Facto.Index" => (in EvaluationContext c) => Value.FromInt64(c.Line.Facto.Index),
        "Facto.RelativeIndex" => (in EvaluationContext c) => Value.FromInt64(c.Line.Facto.RelativeIndex),
        "Facto.IndexDistance" => (in EvaluationContext c) => Value.FromFloat(c.Line.Facto.IndexDistance),
        "ViewportDistance" => (in EvaluationContext c) => Value.FromFloat(c.Line.ViewportDistance),
        "IsActive" => (in EvaluationContext c) => Value.FromBoolean(c.Line.IsActive),
        "IsStarted" => (in EvaluationContext c) => Value.FromBoolean(c.Line.IsStarted),
        "IsFinished" => (in EvaluationContext c) => Value.FromBoolean(c.Line.IsFinished),
        "IsHovered" => (in EvaluationContext c) => Value.FromBoolean(c.Line.IsHovered),
        "IsHidden" => (in EvaluationContext c) => Value.FromBoolean(c.Line.IsHidden),
        "IsText" => (in EvaluationContext c) => Value.FromBoolean(c.Line.IsText),
        "StartMs" => (in EvaluationContext c) => Value.FromInt64(c.Line.StartMs),
        "EndMs" => (in EvaluationContext c) => Value.FromInt64(c.Line.EndMs),
        "DurationMs" => (in EvaluationContext c) => Value.FromInt64(c.Line.DurationMs),
        "Progress" => (in EvaluationContext c) => Value.FromFloat(c.Line.Progress),
        "Width" => (in EvaluationContext c) => Value.FromFloat(c.Line.Width),
        "Height" => (in EvaluationContext c) => Value.FromFloat(c.Line.Height),
        "AnchorX" => (in EvaluationContext c) => Value.FromFloat(c.Line.AnchorX),
        "AnchorY" => (in EvaluationContext c) => Value.FromFloat(c.Line.AnchorY),
        "Text" => (in EvaluationContext c) => Value.FromText(c.Line.Text),
        "IdleColor" => (in EvaluationContext c) => Value.FromColor(c.Line.IdleColor),
        "FocusingColor" => (in EvaluationContext c) => Value.FromColor(c.Line.FocusingColor),
        "IdleColor.A" => (in EvaluationContext c) => Value.FromInt64(c.Line.IdleColor.A),
        "IdleColor.R" => (in EvaluationContext c) => Value.FromInt64(c.Line.IdleColor.R),
        "IdleColor.G" => (in EvaluationContext c) => Value.FromInt64(c.Line.IdleColor.G),
        "IdleColor.B" => (in EvaluationContext c) => Value.FromInt64(c.Line.IdleColor.B),
        "FocusingColor.A" => (in EvaluationContext c) => Value.FromInt64(c.Line.FocusingColor.A),
        "FocusingColor.R" => (in EvaluationContext c) => Value.FromInt64(c.Line.FocusingColor.R),
        "FocusingColor.G" => (in EvaluationContext c) => Value.FromInt64(c.Line.FocusingColor.G),
        "FocusingColor.B" => (in EvaluationContext c) => Value.FromInt64(c.Line.FocusingColor.B),
        "Id" => (in EvaluationContext c) => Value.FromText(c.Line.Id),
        "ParentLineId" => (in EvaluationContext c) => Value.FromText(c.Line.ParentLineId),
        "LineStyle" => (in EvaluationContext c) => Value.FromText(c.Line.LineStyle),
        "Comment" => (in EvaluationContext c) => Value.FromText(c.Line.Comment),
        "RawText" => (in EvaluationContext c) => Value.FromText(c.Line.RawText),
        "Transliteration" => (in EvaluationContext c) => Value.FromText(c.Line.Transliteration),
        "Translation" => (in EvaluationContext c) => Value.FromText(c.Line.Translation),
        "Style.Exists" => (in EvaluationContext c) => Value.FromBoolean(c.Line.Style.Exists),
        "Style.Position" => (in EvaluationContext c) => Value.FromText(c.Line.Style.Position),
        "Style.HasColor" => (in EvaluationContext c) => Value.FromBoolean(c.Line.Style.HasColor),
        "Style.Color" => (in EvaluationContext c) => Value.FromColor(c.Line.Style.Color),
        "Style.Color.A" => (in EvaluationContext c) => Value.FromInt64(c.Line.Style.Color.A),
        "Style.Color.R" => (in EvaluationContext c) => Value.FromInt64(c.Line.Style.Color.R),
        "Style.Color.G" => (in EvaluationContext c) => Value.FromInt64(c.Line.Style.Color.G),
        "Style.Color.B" => (in EvaluationContext c) => Value.FromInt64(c.Line.Style.Color.B),
        "Style.Accent" => (in EvaluationContext c) => Value.FromText(c.Line.Style.Accent),
        "Style.HiddenOnBlur" => (in EvaluationContext c) => Value.FromBoolean(c.Line.Style.HiddenOnBlur),
        _ => throw new NotSupportedException()
    };

    private static Node CompileFrameMember(string path) => path switch
    {
        "CurrentLineIndex" => (in EvaluationContext c) => Value.FromInt64(c.Frame.CurrentLineIndex),
        "CurrentTimeMs" => (in EvaluationContext c) => Value.FromInt64(c.Frame.CurrentTimeMs),
        "RenderTimeMs" => (in EvaluationContext c) => Value.FromInt64(c.Frame.RenderTimeMs),
        "IsPlaying" => (in EvaluationContext c) => Value.FromBoolean(c.Frame.IsPlaying),
        "IsScrolling" => (in EvaluationContext c) => Value.FromBoolean(c.Frame.IsScrolling),
        "IsSeeking" => (in EvaluationContext c) => Value.FromBoolean(c.Frame.IsSeeking),
        "ScrollOffset" => (in EvaluationContext c) => Value.FromFloat(c.Frame.ScrollOffset),
        "ViewWidth" => (in EvaluationContext c) => Value.FromFloat(c.Frame.ViewWidth),
        "ViewHeight" => (in EvaluationContext c) => Value.FromFloat(c.Frame.ViewHeight),
        "Dpi" => (in EvaluationContext c) => Value.FromFloat(c.Frame.Dpi),
        "Bpm" => (in EvaluationContext c) => Value.FromFloat(c.Frame.Bpm),
        _ => throw new NotSupportedException()
    };

    private static Node CompileTextMember(string path) => path switch
    {
        "IsLyric" => (in EvaluationContext c) => Value.FromBoolean(c.Text.IsLyric),
        "IsTransliteration" => (in EvaluationContext c) => Value.FromBoolean(c.Text.IsTransliteration),
        "IsTranslation" => (in EvaluationContext c) => Value.FromBoolean(c.Text.IsTranslation),
        _ => throw new NotSupportedException()
    };

    private static Node CompileWordMember(string path) => path switch
    {
        "Exists" => (in EvaluationContext c) => Value.FromBoolean(c.Word.Exists),
        "Index" => (in EvaluationContext c) => Value.FromInt64(c.Word.Index),
        "Count" => (in EvaluationContext c) => Value.FromInt64(c.Word.Count),
        "StartTimeMs" => (in EvaluationContext c) => Value.FromInt64(c.Word.StartTimeMs),
        "EndTimeMs" => (in EvaluationContext c) => Value.FromInt64(c.Word.EndTimeMs),
        "DurationMs" => (in EvaluationContext c) => Value.FromInt64(c.Word.DurationMs),
        "Progress" => (in EvaluationContext c) => Value.FromFloat(c.Word.Progress),
        "IsInferred" => (in EvaluationContext c) => Value.FromBoolean(c.Word.IsInferred),
        _ => throw new NotSupportedException()
    };

    private static Node CompileGlyphMember(string path) => path switch
    {
        "Index" => (in EvaluationContext c) => Value.FromInt64(c.Glyph.Index),
        "Count" => (in EvaluationContext c) => Value.FromInt64(c.Glyph.Count),
        "IndexInWord" => (in EvaluationContext c) => Value.FromInt64(c.Glyph.IndexInWord),
        "CountInWord" => (in EvaluationContext c) => Value.FromInt64(c.Glyph.CountInWord),
        "RevealProgress" => (in EvaluationContext c) => Value.FromFloat(c.Glyph.RevealProgress),
        "LiftProgress" => (in EvaluationContext c) => Value.FromFloat(c.Glyph.LiftProgress),
        "VisualLeftDip" => (in EvaluationContext c) => Value.FromFloat(c.Glyph.VisualLeftDip),
        "VisualTopDip" => (in EvaluationContext c) => Value.FromFloat(c.Glyph.VisualTopDip),
        "VisualRightDip" => (in EvaluationContext c) => Value.FromFloat(c.Glyph.VisualRightDip),
        "VisualBottomDip" => (in EvaluationContext c) => Value.FromFloat(c.Glyph.VisualBottomDip),
        "VisualWidthDip" => (in EvaluationContext c) => Value.FromFloat(c.Glyph.VisualWidthDip),
        "VisualHeightDip" => (in EvaluationContext c) => Value.FromFloat(c.Glyph.VisualHeightDip),
        _ => throw new NotSupportedException()
    };

    private static NotSupportedException Unsupported(Expression expression) =>
        new($"Unsupported lyric expression node: {expression.NodeType} ({expression.Type.Name}).");

    private readonly record struct EvaluationContext(
        LyricExpressionLine Line,
        LyricExpressionFrame Frame,
        FocusedTextExpressionText Text,
        FocusedTextExpressionWord Word,
        FocusedTextExpressionGlyph Glyph,
        LyricExpressionFunctions Functions)
    {
        public static EvaluationContext WholeLine(
            LyricExpressionLine line,
            LyricExpressionFrame frame,
            LyricExpressionFunctions functions) =>
            new(line, frame, default, default, default, functions);

        public static EvaluationContext Focused(
            LyricExpressionLine line,
            LyricExpressionFrame frame,
            FocusedTextExpressionText text,
            FocusedTextExpressionWord word,
            FocusedTextExpressionGlyph glyph,
            LyricExpressionFunctions functions) =>
            new(line, frame, text, word, glyph, functions);
    }

    private enum ValueKind : byte
    {
        Null,
        Float,
        Int64,
        Boolean,
        Text,
        Color
    }

    private readonly struct Value
    {
        private readonly float _float;
        private readonly long _int64;
        private readonly bool _boolean;
        private readonly string? _text;
        private readonly LyricColorValue _color;

        private Value(
            ValueKind kind,
            float floatValue = 0,
            long int64Value = 0,
            bool booleanValue = false,
            string? textValue = null,
            LyricColorValue colorValue = default)
        {
            Kind = kind;
            _float = floatValue;
            _int64 = int64Value;
            _boolean = booleanValue;
            _text = textValue;
            _color = colorValue;
        }

        public ValueKind Kind { get; }

        public static Value FromFloat(float value) => new(ValueKind.Float, floatValue: value);
        public static Value FromInt64(long value) => new(ValueKind.Int64, int64Value: value);
        public static Value FromBoolean(bool value) => new(ValueKind.Boolean, booleanValue: value);
        public static Value FromText(string? value) => value is null ? default : new(ValueKind.Text, textValue: value);
        public static Value FromColor(LyricColorValue value) => new(ValueKind.Color, colorValue: value);

        public static Value FromConstant(object? value) => value switch
        {
            null => default,
            float number => FromFloat(number),
            double number => FromFloat((float)number),
            int number => FromInt64(number),
            long number => FromInt64(number),
            byte number => FromInt64(number),
            bool boolean => FromBoolean(boolean),
            string text => FromText(text),
            LyricColorValue color => FromColor(color),
            _ => throw new NotSupportedException()
        };

        public float AsFloat() => Kind switch
        {
            ValueKind.Float => _float,
            ValueKind.Int64 => _int64,
            _ => throw new InvalidOperationException($"{Kind} 不能转换为浮点数。")
        };

        public long AsInt64() => Kind switch
        {
            ValueKind.Int64 => _int64,
            ValueKind.Float => (long)_float,
            _ => throw new InvalidOperationException($"{Kind} 不能转换为整数。")
        };

        public bool AsBoolean() => Kind == ValueKind.Boolean
            ? _boolean
            : throw new InvalidOperationException($"{Kind} 不能转换为布尔值。");

        public string? AsText() => Kind switch
        {
            ValueKind.Null => null,
            ValueKind.Text => _text,
            _ => throw new InvalidOperationException($"{Kind} 不能转换为文本。")
        };

        public LyricColorValue AsColor() => Kind == ValueKind.Color
            ? _color
            : throw new InvalidOperationException($"{Kind} 不能转换为颜色。");
    }
}
