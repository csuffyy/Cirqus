﻿using System;
using System.Collections.Generic;
using System.Linq;
using d60.Cirqus.Aggregates;
using d60.Cirqus.Commands;
using d60.Cirqus.Events;
using d60.Cirqus.Extensions;
using EnergyProjects.Tests.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace d60.Cirqus.Testing
{
    public abstract class CirqusTestsHarness
    {
        public static Func<IWriter> Writer = () => new ConsoleWriter();

        protected const string checkmark = "\u221A";
        protected const string cross = "\u2717";

        Stack<TypedId> ids;
        TextFormatter formatter;

        IEnumerable<DomainEvent> results;
        JsonSerializerSettings settings;

        protected TestContext Context { get; private set; }

        protected void Begin(TestContext context)
        {
            ids = new Stack<TypedId>();

            settings = new JsonSerializerSettings
            {
                ContractResolver = new ContractResolver(),
                TypeNameHandling = TypeNameHandling.Objects,
                Formatting = Formatting.Indented,
            };

            Context = context;

            formatter = new TextFormatter(Writer());
        }


        public void End(bool isInExceptionalState)
        {
            // only if we are _not_ in an exceptional state
            if (isInExceptionalState)
            {
                AssertAllEventsExpected();
            }
        }

        protected abstract void Fail();

        protected void Emit<T>(params DomainEvent<T>[] events) where T : AggregateRoot
        {
            Emit(Latest<T>(), events);
        }

        protected void Emit<T>(string id, params DomainEvent<T>[] events) where T : AggregateRoot
        {
            foreach (var @event in events)
            {
                Emit(id, @event);
            }
        }

        void Emit<T>(string id, DomainEvent<T> @event) where T : AggregateRoot
        {
            @event.Meta[DomainEvent.MetadataKeys.AggregateRootId] = id;

            //SetupAuthenticationMetadata(@event.Meta);

            Context.Save(@event);

            formatter
                .Block("Given that:")
                .Write(@event, new EventFormatter(formatter))
                .NewLine()
                .NewLine();
        }

        protected void When(ExecutableCommand command)
        {
            //SetupAuthenticationMetadata(command.Meta);
            //command.IdGenerator = new FakeIdGenerator(this);

            formatter
                .Block("When users:")
                .Write(command, new EventFormatter(formatter));

            results = Context.ProcessCommand(command);
        }

        // ReSharper disable once InconsistentNaming
        protected void Throws<T>(ExecutableCommand When) where T : Exception
        {
            Exception exceptionThrown = null;
            try
            {
                this.When(When);
            }
            catch (Exception e)
            {
                exceptionThrown = e;
            }

            formatter.Block("Then:");

            Assert(
                exceptionThrown is T,
                () => formatter.Write("It throws " + typeof(T).Name).NewLine(),
                () =>
                {
                    if (exceptionThrown == null)
                    {
                        formatter.Write("But it did not.");
                        return;
                    }

                    formatter.Write("But got " + exceptionThrown.GetType().Name).NewLine();
                });


            // consume all events
            results = Enumerable.Empty<DomainEvent>();
        }

        // ReSharper disable once InconsistentNaming
        protected void Throws<T>(string message, ExecutableCommand When) where T : Exception
        {
            Exception exceptionThrown = null;

            try
            {
                this.When(When);
            }
            catch (Exception e)
            {
                exceptionThrown = e;
            }

            formatter.Block("Then:");

            Assert(
                exceptionThrown is T && exceptionThrown.Message == message,
                () => formatter
                    .Write("It throws " + typeof(T).Name).NewLine()
                    .Indent().Write("Message: \"" + message + "\"").Unindent()
                    .NewLine(),
                () =>
                {
                    if (exceptionThrown == null)
                    {
                        formatter.Write("But it did not.");
                        return;
                    }

                    formatter.Write("But got " + exceptionThrown.GetType().Name).NewLine()
                        .Indent().Write("Message: \"" + exceptionThrown.Message + "\"").Unindent();
                });

            // consume all events
            results = Enumerable.Empty<DomainEvent>();
        }

        protected void Then<T>() where T : DomainEvent
        {
            var next = results.FirstOrDefault();

            formatter.Block("Then:");
            
            Assert(
                next is T, 
                () => formatter.Write(typeof(T).Name).NewLine(), 
                () => formatter.Write("But we got " + next.GetType().Name).NewLine());

            // consume one event
            results = results.Skip(1);
        }

        protected void Then<T>(params DomainEvent<T>[] events) where T : AggregateRoot
        {
            Then(Latest<T>(), events);
        }

        protected void Then<T>(string id, params DomainEvent<T>[] events) where T : AggregateRoot
        {
            if (events.Length == 0) return;

            formatter.Block("Then:");

            foreach (var pair in results.Zip(events, (actual, expected) => new { actual, expected }))
            {
                var actual = pair.actual;
                var expected = pair.expected;

                var jActual = JObject.FromObject(actual, JsonSerializer.Create(settings));
                var jExpected = JObject.FromObject(expected, JsonSerializer.Create(settings));

                Assert(
                    actual.GetAggregateRootId().Equals(id) && JToken.DeepEquals(jActual, jExpected),
                    () => formatter.Write(expected, new EventFormatter(formatter)).NewLine(),
                    () =>
                    {
                        formatter.Block("But we got this:")
                            .Indent().Write(actual, new EventFormatter(formatter)).Unindent()
                            .EndBlock();

                        var differ = new Differ();
                        var diffs = differ.LineByLine(jActual.ToString(), jExpected.ToString());
                        var diff = differ.PrettyLineByLine(diffs);

                        formatter
                            .NewLine().NewLine()
                            .Write("Diff:").NewLine()
                            .Write(diff);
                    });
            }

            // consume events
            results = results.Skip(events.Length);
        }

        protected void ThenNo<T>() where T : DomainEvent
        {
            formatter.Block("Then:");

            var eventsOfType = results.OfType<T>().ToList();

            Assert(
                !eventsOfType.Any(),
                () => formatter.Write(string.Format("No {0} is emitted", typeof(T).Name)),
                () =>
                {
                    formatter.Block("But we got this:");
                    foreach (var @event in eventsOfType)
                    {
                        formatter.Write(@event, new EventFormatter(formatter)).NewLine();
                    }
                    formatter.EndBlock();
                });

            // consume all events
            results = Enumerable.Empty<DomainEvent>();
        }

        protected string NewId<T>(params object[] args)
        {
            var id = Guid.NewGuid().ToString();
            ids.Push(new TypedId<T>(id));
            return id;
        }

        protected string Id<T>() where T : class
        {
            return Id<T>(1);
        }

        protected string Id<T>(int index) where T : class
        {
            var array = ids.OfType<TypedId<T>>().Reverse().ToArray();
            if (array.Length < index)
            {
                throw new IndexOutOfRangeException(String.Format("Could not find Id<{0}> with index {1}", typeof(T).Name, index));
            }

            return array[index - 1].ToString();
        }

        protected string Latest<T>() where T : AggregateRoot
        {
            string id;
            if (!TryGetLatest<T>(out id))
                throw new InvalidOperationException(string.Format("Can not get latest {0} id, since none exists.", typeof(T).Name));

            return id;
        }

        protected bool TryGetLatest<T>(out string latest) where T : AggregateRoot
        {
            latest = null;

            var lastestOfType = ids.OfType<TypedId<T>>().ToList();
            if (lastestOfType.Any())
            {
                latest = lastestOfType.First().ToString();
                return true;
            }

            return false;
        }

        void AssertAllEventsExpected()
        {
            if (results != null && results.Any())
            {
                Assert(false, () => formatter.Write("Expects no more events").NewLine(), () =>
                {
                    formatter.Write("But found:").NewLine().Indent();
                    foreach (var @event in results)
                    {
                        formatter.Write(@event, new EventFormatter(formatter));
                    }
                    formatter.Unindent();
                });
            }
        }

        void Assert(bool condition, Action writeExpected, Action onFail)
        {
            if (condition)
            {
                formatter.Write(checkmark + " ").Indent();
                writeExpected();
                formatter.Unindent().NewLine();
            }
            else
            {
                formatter.Write(cross + " ").Indent();
                writeExpected();
                formatter.Unindent().NewLine();

                onFail();

                Fail();
            }
        }

        //protected void SetupAuthenticationMetadata(Metadata meta)
        //{
        //    Id<Company> latestCompanyId;
        //    if (TryGetLatest(out latestCompanyId))
        //        meta[MetadataEx.CompanyIdMetadataKey] = latestCompanyId.ToString();

        //    Id<User> latestUserId;
        //    if (TryGetLatest(out latestUserId))
        //        meta[MetadataEx.UserIdMetadataKey] = latestUserId.ToString();
        //}



        //public class FakeIdGenerator : IdGenerator
        //{
        //    readonly CommandTests self;

        //    public FakeIdGenerator(CommandTests self)
        //    {
        //        this.self = self;
        //    }

        //    public virtual Id<T> NewId<T>(params object[] args)
        //    {
        //        return self.NewId<T>(args);
        //    }
        //}

        protected interface TypedId
        {
             
        }

        class TypedId<T> : TypedId
        {
            readonly string id;

            public TypedId(string id)
            {
                this.id = id;
            }

            public override string ToString()
            {
                return id;
            }
        }

        class ContractResolver : DefaultContractResolver
        {
            protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
            {
                var jsonProperties = base.CreateProperties(type, memberSerialization)
                    .Where(property => property.DeclaringType != typeof(DomainEvent) && property.PropertyName != "Meta")
                    .ToList();

                return jsonProperties;
            }
        }
    }
}
