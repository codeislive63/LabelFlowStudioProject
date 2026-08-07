using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using LabelFlowStudio.Desktop;
using Microsoft.Web.WebView2.Core;

namespace LabelFlowStudio.Application.Tests.Desktop.Views;

public sealed class TemplatePreviewInteractivePrintTests
{
    public static TheoryData<Type> PreviewWindowTypes =>
    [
        typeof(EndLabelTemplatePreviewWindow),
        typeof(StuffingSheetTemplatePreviewWindow)
    ];

    [Theory]
    [MemberData(nameof(PreviewWindowTypes))]
    public void InteractivePrint_AlwaysUsesBrowserDialogAndHasNoDirectPrinterPath(Type windowType)
    {
        const BindingFlags privateStatic = BindingFlags.NonPublic | BindingFlags.Static;
        const BindingFlags privateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        var dialogKindField = windowType.GetField("InteractivePrintDialogKind", privateStatic);
        Assert.NotNull(dialogKindField);
        Assert.True(dialogKindField.IsLiteral);
        Assert.Equal(
            (int)CoreWebView2PrintDialogKind.Browser,
            Convert.ToInt32(dialogKindField.GetRawConstantValue()));

        Assert.Null(windowType.GetMethod("TryPrintToConfiguredPrinterAsync", privateStatic));
        Assert.Null(windowType.GetMethod("TryPrintToPreferredPrinterAsync", privateStatic));

        var printMethod = windowType.GetMethod("PrintAsync", privateInstance);
        Assert.NotNull(printMethod);

        var stateMachine = printMethod
            .GetCustomAttribute<AsyncStateMachineAttribute>()?
            .StateMachineType;
        Assert.NotNull(stateMachine);

        var moveNext = stateMachine.GetMethod("MoveNext", privateInstance);
        Assert.NotNull(moveNext);

        var instructions = ReadInstructions(moveNext).ToArray();

        Assert.Contains(
            instructions,
            instruction => instruction.Operand is MethodBase method
                && method.DeclaringType == typeof(CoreWebView2)
                && method.Name == nameof(CoreWebView2.ShowPrintUI));

        Assert.DoesNotContain(
            instructions,
            instruction => instruction.Operand is MethodBase method
                && method.DeclaringType == typeof(CoreWebView2)
                && method.Name == nameof(CoreWebView2.PrintAsync));

        Assert.DoesNotContain(
            instructions,
            instruction => instruction.Operand is MethodBase method
                && method.DeclaringType?.Name.Contains("SilentHtmlPrinter", StringComparison.Ordinal) == true);

        Assert.DoesNotContain(
            instructions,
            instruction => instruction.Operand is string text
                && (text.Contains("Отправлено на принтер", StringComparison.Ordinal)
                    || text.Contains("Печать выполнена", StringComparison.Ordinal)));
    }

    private static IEnumerable<IlInstruction> ReadInstructions(MethodInfo method)
    {
        var body = method.GetMethodBody();
        Assert.NotNull(body);

        var bytes = body.GetILAsByteArray();
        Assert.NotNull(bytes);

        var position = 0;
        while (position < bytes.Length)
        {
            var opCode = ReadOpCode(bytes, ref position);
            var operand = ReadOperand(method, opCode, bytes, ref position);
            yield return new IlInstruction(opCode, operand);
        }
    }

    private static OpCode ReadOpCode(byte[] bytes, ref int position)
    {
        var first = bytes[position++];
        var value = first == 0xFE
            ? unchecked((short)(0xFE00 | bytes[position++]))
            : first;

        return OpCodesByValue[value];
    }

    private static object? ReadOperand(
        MethodInfo method,
        OpCode opCode,
        byte[] bytes,
        ref int position)
    {
        switch (opCode.OperandType)
        {
            case OperandType.InlineNone:
                return null;

            case OperandType.ShortInlineBrTarget:
            case OperandType.ShortInlineI:
            case OperandType.ShortInlineVar:
                position += 1;
                return null;

            case OperandType.InlineVar:
                position += 2;
                return null;

            case OperandType.InlineBrTarget:
            case OperandType.InlineField:
            case OperandType.InlineI:
            case OperandType.InlineSig:
            case OperandType.InlineTok:
            case OperandType.InlineType:
                position += 4;
                return null;

            case OperandType.ShortInlineR:
                position += 4;
                return null;

            case OperandType.InlineI8:
            case OperandType.InlineR:
                position += 8;
                return null;

            case OperandType.InlineString:
            {
                var token = ReadToken(bytes, ref position);
                return method.Module.ResolveString(token);
            }

            case OperandType.InlineMethod:
            {
                var token = ReadToken(bytes, ref position);
                return method.Module.ResolveMethod(
                    token,
                    method.DeclaringType?.GetGenericArguments(),
                    method.GetGenericArguments());
            }

            case OperandType.InlineSwitch:
            {
                var caseCount = ReadToken(bytes, ref position);
                position += caseCount * sizeof(int);
                return null;
            }

            default:
                throw new NotSupportedException($"Unsupported IL operand type: {opCode.OperandType}");
        }
    }

    private static int ReadToken(byte[] bytes, ref int position)
    {
        var token = BitConverter.ToInt32(bytes, position);
        position += sizeof(int);
        return token;
    }

    private static readonly IReadOnlyDictionary<short, OpCode> OpCodesByValue =
        typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opCode => opCode.Value);

    private sealed record IlInstruction(OpCode OpCode, object? Operand);
}
