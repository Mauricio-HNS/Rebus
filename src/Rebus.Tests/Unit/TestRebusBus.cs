﻿using System;
using System.Threading;
using NUnit.Framework;
using Rebus.Bus;
using Rebus.Messages;
using Rhino.Mocks;

namespace Rebus.Tests.Unit
{
    [TestFixture]
    public class TestRebusBus : FixtureBase
    {
        RebusBus bus;
        IReceiveMessages receiveMessages;
        IActivateHandlers activateHandlers;
        IDetermineDestination determineDestination;
        ISendMessages sendMessages;

        protected override void DoSetUp()
        {
            receiveMessages = Mock<IReceiveMessages>();
            activateHandlers = Mock<IActivateHandlers>();
            determineDestination = Mock<IDetermineDestination>();
            sendMessages = Mock<ISendMessages>();
            bus = new RebusBus(activateHandlers,
                               sendMessages,
                               receiveMessages,
                               Mock<IStoreSubscriptions>(),
                               determineDestination);
        }

        protected override void DoTearDown()
        {
            bus.Dispose();
        }

        [Test]
        public void SendsMessagesToTheRightDestination()
        {
            // arrange
            determineDestination.Stub(d => d.GetEndpointFor(typeof (PolymorphicMessage))).Return("woolala");
            var theMessageThatWasSent = new PolymorphicMessage();

            // act
            bus.Send(theMessageThatWasSent);

            // assert
            sendMessages
                .AssertWasCalled(s => s.Send(Arg<string>.Is.Equal("woolala"),
                                             Arg<TransportMessage>.Matches(t => t.Messages[0] == theMessageThatWasSent)));
        }

        [Test]
        public void SubscribesToMessagesFromTheRightPublisher()
        {
            // arrange
            determineDestination.Stub(d => d.GetEndpointFor(typeof(PolymorphicMessage))).Return("woolala");
            receiveMessages.Stub(r => r.InputQueue).Return("my input queue");

            // act
            bus.Subscribe<PolymorphicMessage>();

            // assert
            sendMessages
                .AssertWasCalled(s => s.Send(Arg<string>.Is.Equal("woolala"),
                                             Arg<TransportMessage>.Matches(
                                                 t => t.Headers["returnAddress"] == "my input queue" &&
                                                 ((SubscriptionMessage) t.Messages[0]).Type ==
                                                 typeof (PolymorphicMessage).FullName)));
        }

        [Test]
        public void CanDoPolymorphicMessageDispatch()
        {
            receiveMessages.Stub(r => r.ReceiveMessage())
                .Return(new TransportMessage
                            {
                                Id = "some id",
                                Messages = new object[]
                                               {
                                                   new PolymorphicMessage()
                                               }
                            });

            var manualResetEvent = new ManualResetEvent(false);

            var handler = new SomeHandler(manualResetEvent);
            
            activateHandlers.Stub(f => f.GetHandlerInstancesFor<IFirstInterface>())
                .Return(new[] {(IHandleMessages<IFirstInterface>) handler});
            
            activateHandlers.Stub(f => f.GetHandlerInstancesFor<ISecondInterface>())
                .Return(new[] {(IHandleMessages<ISecondInterface>) handler});
            
            activateHandlers.Stub(f => f.GetHandlerInstancesFor<PolymorphicMessage>())
                .Return(new IHandleMessages<PolymorphicMessage>[0]);

            bus.Start();

            if (!manualResetEvent.WaitOne(TimeSpan.FromSeconds(1)))
            {
                Assert.Fail("Did not receive messages withing timeout");
            }

            Assert.That(handler.FirstMessageHandled, Is.True);
            Assert.That(handler.SecondMessageHandled, Is.True);
        }

        class SomeHandler : IHandleMessages<IFirstInterface>, IHandleMessages<ISecondInterface>
        {
            readonly ManualResetEvent manualResetEvent;

            public SomeHandler(ManualResetEvent manualResetEvent)
            {
                this.manualResetEvent = manualResetEvent;
            }

            public bool FirstMessageHandled { get; set; }
            public bool SecondMessageHandled { get; set; }

            public void Handle(IFirstInterface message)
            {
                FirstMessageHandled = true;
                PossiblyRaiseEvent();
            }

            public void Handle(ISecondInterface message)
            {
                SecondMessageHandled = true;
                PossiblyRaiseEvent();
            }

            void PossiblyRaiseEvent()
            {
                if (FirstMessageHandled && SecondMessageHandled)
                {
                    manualResetEvent.Set();
                }
            }
        }

        interface IFirstInterface {}
        interface ISecondInterface {}
        class PolymorphicMessage : IFirstInterface, ISecondInterface{}
    }
}