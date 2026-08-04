using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Changsta.Ai.Interface.Api.ViewModels;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Api
{
    /// <summary>
    /// Every public property on a view model is a published field of the API, and clients key off
    /// the serialised names. These walk the whole ViewModels namespace by reflection so a property
    /// added to a view model cannot silently fail to reach the wire.
    /// <para>
    /// Written after <c>RadioSlotVm.RelaxedRules</c> shipped as a computed value that
    /// <c>RadioScheduler</c> populated, <c>GetRadioScheduleUseCase</c> carried onto the DTO, and
    /// <c>RadioController.MapSlot</c> never mapped — a dead data path that 746 tests did not see,
    /// because every one of them asserted mechanics rather than the contract.
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class ViewModelWireContractTests
    {
        private static readonly JsonSerializerOptions WireOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        /// <summary>Every concrete view model in the shipped namespace.</summary>
        private static IEnumerable<Type> ViewModels =>
            typeof(RadioSlotVm).Assembly
                .GetTypes()
                .Where(t => t.Namespace == typeof(RadioSlotVm).Namespace)
                .Where(t => t.IsClass && !t.IsAbstract && !t.IsGenericType)
                .Where(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Length > 0)
                .OrderBy(t => t.Name);

        [TestCaseSource(nameof(ViewModels))]
        public void Every_public_property_reaches_the_wire(Type viewModel)
        {
            object instance = Populate(viewModel);
            string json = JsonSerializer.Serialize(instance, viewModel, WireOptions);

            using JsonDocument document = JsonDocument.Parse(json);
            var present = document.RootElement.EnumerateObject()
                .Select(p => p.Name)
                .ToHashSet(StringComparer.Ordinal);

            string[] expected = viewModel
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetMethod is not null)
                .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name))
                .ToArray();

            foreach (string name in expected)
            {
                present.Should().Contain(
                    name,
                    because: $"{viewModel.Name}.{name} is a public property and therefore a published API field");
            }
        }

        [TestCaseSource(nameof(ViewModels))]
        public void No_property_serialises_under_a_pascal_case_name(Type viewModel)
        {
            string json = JsonSerializer.Serialize(Populate(viewModel), viewModel, WireOptions);

            using JsonDocument document = JsonDocument.Parse(json);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                char first = property.Name[0];
                char.IsUpper(first).Should().BeFalse(
                    because: $"{viewModel.Name}.{property.Name} would break camelCase clients");
            }
        }

        /// <summary>
        /// Fills every settable property with a non-default value. A property left at its default
        /// can serialise away under an ignore condition, which is exactly the silence being tested
        /// for, so nothing here may be left unset.
        /// </summary>
        private static object Populate(Type type)
        {
            object instance = FormatterServicesCreate(type);

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.SetMethod is null)
                {
                    continue;
                }

                object? value = SampleFor(property.PropertyType);
                if (value is not null)
                {
                    property.SetValue(instance, value);
                }
            }

            return instance;
        }

        private static object FormatterServicesCreate(Type type) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);

        private static object? SampleFor(Type type)
        {
            Type target = Nullable.GetUnderlyingType(type) ?? type;

            if (target == typeof(string))
            {
                return "sample";
            }

            if (target == typeof(bool))
            {
                return true;
            }

            if (target == typeof(int))
            {
                return 7;
            }

            if (target == typeof(long))
            {
                return 7L;
            }

            if (target == typeof(double))
            {
                return 0.5d;
            }

            if (target == typeof(DateTimeOffset))
            {
                return new DateTimeOffset(2026, 8, 4, 9, 0, 0, TimeSpan.Zero);
            }

            if (target == typeof(DateTime))
            {
                return new DateTime(2026, 8, 4, 9, 0, 0, DateTimeKind.Utc);
            }

            if (target.IsEnum)
            {
                return Enum.GetValues(target).GetValue(0);
            }

            if (target.IsArray)
            {
                Type? element = target.GetElementType();
                if (element is null)
                {
                    return null;
                }

                Array array = Array.CreateInstance(element, 1);
                array.SetValue(SampleFor(element) ?? FormatterServicesCreate(element), 0);
                return array;
            }

            if (target.IsGenericType && typeof(IEnumerable).IsAssignableFrom(target))
            {
                Type element = target.GetGenericArguments()[0];
                Array array = Array.CreateInstance(element, 1);
                array.SetValue(SampleFor(element) ?? FormatterServicesCreate(element), 0);
                return array;
            }

            if (target.IsClass)
            {
                return Populate(target);
            }

            return null;
        }
    }
}
