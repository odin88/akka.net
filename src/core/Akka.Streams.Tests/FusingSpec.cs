﻿//-----------------------------------------------------------------------
// <copyright file="FusingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.Fusing;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests
{
    public class FusingSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FusingSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        private static object GetInstanceField(Type type, object instance, string fieldName)
        {
            const BindingFlags bindFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            var field = type.GetField(fieldName, bindFlags);
            return field.GetValue(instance);
        }
        
        [Fact]
        public async Task A_SubFusingActorMaterializer_must_work_with_asynchronous_boundaries_in_the_subflows()
        {
            var async = Flow.Create<int>().Select(x => x*2).Async();
            var t = Source.From(Enumerable.Range(0, 10))
                .Select(x => x*10)
                .MergeMany(5, i => Source.From(Enumerable.Range(i, 10)).Via(async))
                .Grouped(1000)
                .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

            await t.ShouldCompleteWithin(3.Seconds());
            t.Result.Distinct().OrderBy(i => i).Should().BeEquivalentTo(Enumerable.Range(0, 199).Where(i => i%2 == 0));
        }

        [Fact]
        public async Task A_SubFusingActorMaterializer_must_use_multiple_actors_when_there_are_asynchronous_boundaries_in_the_subflows_manual ()
        {
            var async = Flow.Create<int>().Select(x =>
            {
                TestActor.Tell(RefFunc());
                return x;
            }).Async();
            var t = Source.From(Enumerable.Range(0, 10))
                .Select(x =>
                {
                    TestActor.Tell(RefFunc());
                    return x;
                })
                .MergeMany(5, i => Source.Single(i).Via(async))
                .Grouped(1000)
                .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

            await t.ShouldCompleteWithin(3.Seconds());
            t.Result.Should().BeEquivalentTo(Enumerable.Range(0, 10));

            var refs = await ReceiveNAsync(20).Distinct().ToListAsync();
            // main flow + 10 sub-flows
            refs.Count.Should().Be(11);
            return;

            string RefFunc()
            {
                var bus = (BusLogging)GraphInterpreter.Current.Log;
                return bus.LogSource;
            }
        }

        [Fact]
        public async Task A_SubFusingActorMaterializer_must_use_multiple_actors_when_there_are_asynchronous_boundaries_in_the_subflows_combinator()
        {
            string RefFunc()
            {
                var bus = (BusLogging)GraphInterpreter.Current.Log;
                return bus.LogSource;
            }

            var flow = Flow.Create<int>().Select(x =>
            {
                TestActor.Tell(RefFunc());
                return x;
            });
            var t = Source.From(Enumerable.Range(0, 10))
                .Select(x =>
                {
                    TestActor.Tell(RefFunc());
                    return x;
                })
                .MergeMany(5, i => Source.Single(i).Via(flow.Async()))
                .Grouped(1000)
                .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

            await t.ShouldCompleteWithin(3.Seconds());
            t.Result.Should().BeEquivalentTo(Enumerable.Range(0, 10));

            var refs = await ReceiveNAsync(20).Distinct().ToListAsync();
            // main flow + 10 sub-flows
            refs.Count.Should().Be(11);
        }
    }
}
