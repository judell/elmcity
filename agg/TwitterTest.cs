/* ********************************************************************************
 *
 * Copyright 2010 Microsoft Corporation
 * 
 * Licensed under the Apache License, Version 2.0 (the "License"); you
 * may not use this file except in compliance with the License. You may
 * obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0 
 * Unless required by applicable law or agreed to in writing, software distributed 
 * under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR
 * CONDITIONS OF ANY KIND, either express or implied. See the License for the 
 * specific language governing permissions and limitations under the License. 
 *
 * *******************************************************************************/

using System.Linq;
using NUnit.Framework;

namespace CalendarAggregator
{
    [TestFixture]
    public class TwitterTest
    {

        [Test]
        public void CannotRefollow()
        {
            var response = TwitterApi.FollowTwitterAccount("judell");
            var message = response.DataAsString();
            Assert.That(message.Contains("already on your list"));
        }

        [Test]
        public void CanRetrieveDirectMessagesFromAzure()
        {
            var messages = TwitterApi.GetDirectMessagesFromAzure();
            var msg = messages.First();
            Assert.That(msg.recipient_screen_name == Configurator.twitter_account);
        }

        [Test]
        public void CanRetrieveDirectMessagesFromTwitter()
        {
            var messages = TwitterApi.GetDirectMessagesFromTwitter(1);
            var msg = messages.First();
            Assert.That(msg.recipient_screen_name == Configurator.twitter_account);
        }
    }
}

