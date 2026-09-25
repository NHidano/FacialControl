using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.InputSystem;
using Hidano.FacialControl.Adapters.Processors;

namespace Hidano.FacialControl.InputSystem.Tests.PlayMode.Adapters.Processors
{
    /// <summary>
    //: <see cref="AnalogProcessorRegistration"/> が 6 種の
    /// <see cref="InputProcessor{TValue}"/> を Editor / Runtime 双方の初期化フェーズで
    /// <see cref="InputSystem.RegisterProcessor{T}(string)"/> 経由で登録していることを検証する。
    /// </summary>
    /// <remarks>
    /// PlayMode 開始時に <see cref="RuntimeInitializeOnLoadMethodAttribute"/> によって
    /// <c>AnalogProcessorRegistration.Register()</c> が走るため、テスト到達時点で全 6 processor が
    /// <c>UnityEngine.InputSystem.InputProcessor.s_Processors</c> に登録済みである必要がある。
    /// </remarks>
    [TestFixture]
    public class AnalogProcessorRegistrationTests
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // PlayMode 入時の RuntimeInitializeOnLoadMethod が確実に実行されているはずだが、
            // 念のため type を参照することで static constructor の発火も保証する。
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(
                typeof(AnalogProcessorRegistration).TypeHandle);
        }

        [Test]
        public void ProcessorNames_HasSixDistinctEntries()
        {
            Assert.AreEqual(6, AnalogProcessorRegistration.ProcessorNames.Length,
                "AnalogProcessorRegistration.ProcessorNames must enumerate exactly 6 processors.");
            CollectionAssert.AllItemsAreUnique(AnalogProcessorRegistration.ProcessorNames);
        }

        [Test]
        public void Register_DeadZoneProcessor_IsResolvableByName()
        {
            Assert.AreEqual(
                typeof(AnalogDeadZoneProcessor),
                LookupRegisteredProcessor(AnalogProcessorRegistration.DeadZoneProcessorName));
        }

        [Test]
        public void Register_ScaleProcessor_IsResolvableByName()
        {
            Assert.AreEqual(
                typeof(AnalogScaleProcessor),
                LookupRegisteredProcessor(AnalogProcessorRegistration.ScaleProcessorName));
        }

        [Test]
        public void Register_OffsetProcessor_IsResolvableByName()
        {
            Assert.AreEqual(
                typeof(AnalogOffsetProcessor),
                LookupRegisteredProcessor(AnalogProcessorRegistration.OffsetProcessorName));
        }

        [Test]
        public void Register_ClampProcessor_IsResolvableByName()
        {
            Assert.AreEqual(
                typeof(AnalogClampProcessor),
                LookupRegisteredProcessor(AnalogProcessorRegistration.ClampProcessorName));
        }

        [Test]
        public void Register_CurveProcessor_IsResolvableByName()
        {
            Assert.AreEqual(
                typeof(AnalogCurveProcessor),
                LookupRegisteredProcessor(AnalogProcessorRegistration.CurveProcessorName));
        }

        [Test]
        public void Register_InvertProcessor_IsResolvableByName()
        {
            Assert.AreEqual(
                typeof(AnalogInvertProcessor),
                LookupRegisteredProcessor(AnalogProcessorRegistration.InvertProcessorName));
        }

        // ============================================================
        // ヘルパー
        // ============================================================

        /// <summary>
        /// InputSystem の <c>InputProcessor.s_Processors</c>（<c>TypeTable</c>）に対して
        /// 指定名で <c>LookupTypeRegistration</c> を呼び、登録された CLR <see cref="Type"/> を返す。
        /// 公開 API では登録一覧を取得できないため、リフレクションで検証する（PlayMode テスト限定）。
        /// </summary>
        private static Type LookupRegisteredProcessor(string name)
        {
            var inputProcessorType = typeof(InputProcessor);
            var sProcessorsField = inputProcessorType.GetField(
                "s_Processors",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assume.That(sProcessorsField, Is.Not.Null,
                "Reflection failed: UnityEngine.InputSystem.InputProcessor.s_Processors not found. " +
                "InputSystem の internal レイアウトが変わった可能性があります。");

            var typeTable = sProcessorsField.GetValue(null);
            Assume.That(typeTable, Is.Not.Null,
                "Reflection failed: InputProcessor.s_Processors value is null.");

            var lookupMethod = typeTable.GetType().GetMethod(
                "LookupTypeRegistration",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(string) },
                modifiers: null);
            Assume.That(lookupMethod, Is.Not.Null,
                "Reflection failed: TypeTable.LookupTypeRegistration(string) not found.");

            return (Type)lookupMethod.Invoke(typeTable, new object[] { name });
        }
    }
}
