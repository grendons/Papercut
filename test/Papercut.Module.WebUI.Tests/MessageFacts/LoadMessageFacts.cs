﻿// Papercut
//
// Copyright © 2008 - 2012 Ken Robertson
// Copyright © 2013 - 2017 Jaben Cargman
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.


namespace Papercut.Module.WebUI.Test.MessageFacts
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Mime;
    using System.Text;
    using System.Threading;

    using Autofac;
    using Base;
    using Message;
    using MimeKit;
    using Models;

    using NUnit.Framework;

    using ContentType = MimeKit.ContentType;

    public class LoadMessageFacts : ApiTestBase
    {
        readonly MessageRepository _messageRepository;

        public LoadMessageFacts()
        {
            this._messageRepository = Scope.Resolve<MessageRepository>();
        }

        [Test, Order(0)]
        public void ShouldReturn404IfNotFoundByID()
        {
            var response = Client.GetAsync("/api/messages/some-strange-id").Result;
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Test, Order(1)]
        public void ShouldLoadAllMessages()
        {
            var existedMail = new MimeMessage
            {
                Subject = "Test",
                From = {new MailboxAddress("mffeng@gmail.com")}
            };

            this._messageRepository.SaveMessage(fs => existedMail.WriteTo(fs));

            var messages = Get<MessageListResponse>("/api/messages").Messages;
            Assert.AreEqual(1, messages.Count);

            var message = messages.First();
            Assert.NotNull(message.Id);
            Assert.NotNull(message.CreatedAt);
            Assert.NotNull(message.Size);
            Assert.AreEqual("Test", message.Subject);


            // Should serve CreatedAt as UTC value.
            var dateDiff = DateTime.UtcNow - message.CreatedAt.Value;
            Assert.Less(Math.Abs(dateDiff.TotalMinutes), 1);
        }

        [Test, Order(2)]
        public void ShouldLoadMessagesByPagination()
        {
            var existedMail = new MimeMessage
            {
                From = {new MailboxAddress("mffeng@gmail.com")}
            };

            // clear out existing messages
            foreach (var message in this._messageRepository.LoadMessages())
            {
                this._messageRepository.DeleteMessage(message);
            }

            for (int i = 0; i < 10; i++)
            {
                existedMail.Subject = $"Test {i+1}";
                this._messageRepository.SaveMessage(fs => existedMail.WriteTo(fs));
                Thread.Sleep(10);
            }

            var messageResponse = Get<MessageListResponse>("/api/messages?limit=2&start=3");
            var messages = messageResponse.Messages;
            Assert.AreEqual(10, messageResponse.TotalMessageCount);
            Assert.AreEqual(2, messages.Count);

            var message1 = messages.First();
            Assert.NotNull(message1.Id);
            Assert.NotNull(message1.CreatedAt);
            Assert.NotNull(message1.Size);
            Assert.AreEqual("Test 7", message1.Subject);

            var message2 = messages.Last();
            Assert.NotNull(message2.Id);
            Assert.NotNull(message2.CreatedAt);
            Assert.NotNull(message2.Size);
            Assert.AreEqual("Test 6", message2.Subject);
        }

        [Test, Order(3)]
        public void ShouldLoadMessageDetailByID()
        {
            var existedMail = new MimeMessage
            {
                Subject = "Test",
                From = {new MailboxAddress("mffeng@gmail.com")},
                To = {new MailboxAddress("xwliu@gmail.com")},
                Cc = {new MailboxAddress("jjchen@gmail.com"), new MailboxAddress("ygma@gmail.com")},
                Bcc = {new MailboxAddress("rzhe@gmail.com"), new MailboxAddress("xueting@gmail.com")},
                Body = new TextPart("plain") {Text = "Hello Buddy"}
            };
            this._messageRepository.SaveMessage(fs => existedMail.WriteTo(fs));
            var messages = Get<MessageListResponse>("/api/messages").Messages;
            var id = messages.First().Id;

            var detail = Get<MimeMessageEntry.DetailDto>($"/api/messages/{id}");
            Assert.AreEqual(id, detail.Id);
            Assert.NotNull(detail.CreatedAt);

            Assert.AreEqual(1, detail.From.Count);
            Assert.AreEqual("mffeng@gmail.com", detail.From.First().Address);


            Assert.AreEqual(1, detail.To.Count);
            Assert.AreEqual("xwliu@gmail.com", detail.To.First().Address);


            Assert.AreEqual(2, detail.Cc.Count);
            Assert.AreEqual("jjchen@gmail.com", detail.Cc.First().Address);
            Assert.AreEqual("ygma@gmail.com", detail.Cc.Last().Address);

            Assert.AreEqual(2, detail.BCc.Count);
            Assert.AreEqual("rzhe@gmail.com", detail.BCc.First().Address);
            Assert.AreEqual("xueting@gmail.com", detail.BCc.Last().Address);

            Assert.AreEqual("Test", detail.Subject);
            Assert.AreEqual("Hello Buddy", detail.TextBody?.Trim());
            Assert.Null(detail.HtmlBody);
        }

        [Test, Order(4)]
        public void ShouldContainHeadersInMessageDetail()
        {
            var existedMail = new MimeMessage
            {
                Subject = "Test",
                From = {new MailboxAddress("mffeng@gmail.com")},
                To = {new MailboxAddress("xwliu@gmail.com")},
                Body = new TextPart("plain") {Text = "Hello Buddy"}
            };
            existedMail.Headers.Add(HeaderId.ReplyTo, "one@replyto.com");
            existedMail.Headers.Add("X-Extended", "extended value");
            this._messageRepository.SaveMessage(fs => existedMail.WriteTo(fs));
            var messages = Get<MessageListResponse>("/api/messages").Messages;
            var id = messages.First().Id;


            var detail = Get<MimeMessageEntry.DetailDto>($"/api/messages/{id}");
            Assert.AreEqual(id, detail.Id);

            var headers = detail.Headers;
            Assert.AreEqual("one@replyto.com",  headers.First(h => h.Name == "Reply-To").Value);
            Assert.AreEqual("extended value", headers.First(h => h.Name == "X-Extended").Value);
        }

        [Test, Order(5)]
        public void ShouldLoadDeatailWithSections()
        {
            var existedMail = new MimeMessage
            {
                Body = new Multipart
                {
                    new MimePart(new ContentType("image", "jpeg") {Charset = Encoding.UTF8.EncodingName})
                    {
                        FileName = "sample.pdf",
                        ContentId = Guid.Empty.ToString()
                    }
                }
            };
            this._messageRepository.SaveMessage(fs => existedMail.WriteTo(fs));

            var messageId = Get<MessageListResponse>("/api/messages").Messages.First().Id;

            var detail = Get<MimeMessageEntry.DetailDto>($"/api/messages/{messageId}");
            Assert.AreEqual(messageId, detail.Id);

            var sections = detail.Sections;
            Assert.AreEqual(1, sections.Count);
            Assert.AreEqual(Guid.Empty.ToString(), sections.First().Id);
            Assert.AreEqual("image/jpeg", sections.First().MediaType);
            Assert.AreEqual("sample.pdf", sections.First().FileName);
        }

        [Test, Order(6)]
        public void ShouldDownloadSectionByIndex()
        {
            var existedMail = new MimeMessage
            {
                Body = new Multipart
                {
                    new MimePart(new ContentType("image", "jpeg") {Charset = Encoding.UTF8.EncodingName})
                    {
                        FileName = "sample.pdf",
                        ContentObject = new ContentObject(
                            new MemoryStream(Encoding.UTF8.GetBytes("Content")), ContentEncoding.Binary)
                    }
                }
            };
            this._messageRepository.SaveMessage(fs => existedMail.WriteTo(fs));

            var messageId = Get<MessageListResponse>("/api/messages").Messages.First().Id;

            var response = Get($"/api/messages/{messageId}/sections/0");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

            var disposition = response.Content.Headers.ContentDisposition;
            Assert.AreEqual(DispositionTypeNames.Attachment, disposition.DispositionType);
            Assert.AreEqual("sample.pdf", disposition.FileName);
            Assert.AreEqual("image/jpeg", response.Content.Headers.ContentType.MediaType);
        }

        [Test, Order(7)]
        public void ShouldDownloadSectionByContentId()
        {
            var contentId = Guid.NewGuid().ToString();
            var existedMail = new MimeMessage
            {
                Body = new Multipart
                {
                    new MimePart(new ContentType("image", "jpeg") {Charset = Encoding.UTF8.EncodingName})
                    {
                        FileName = "sample.pdf",
                        ContentId = contentId,
                        ContentObject = new ContentObject(
                            new MemoryStream(Encoding.UTF8.GetBytes("Content")), ContentEncoding.Binary)
                    }
                }
            };
            this._messageRepository.SaveMessage(fs => existedMail.WriteTo(fs));

            var messageId = Get<MessageListResponse>("/api/messages").Messages.First().Id;

            var response = Get($"/api/messages/{messageId}/contents/{contentId}");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

            var disposition = response.Content.Headers.ContentDisposition;
            Assert.AreEqual(DispositionTypeNames.Attachment, disposition.DispositionType);
            Assert.AreEqual("sample.pdf", disposition.FileName);
            Assert.AreEqual("image/jpeg", response.Content.Headers.ContentType.MediaType);
        }

        [Test, Order(8)]
        public void ShouldDownloadRawMessage()
        {
            var existedMail = new MimeMessage(
                new [] { new MailboxAddress("from@from.com") },
                new[] { new MailboxAddress("to@to.com") },
                 "Sample email",
                 new Multipart
                {
                    new MimePart(new ContentType("text", "html") {Charset = Encoding.UTF8.EncodingName})
                    {
                        ContentObject = new ContentObject(new MemoryStream(Encoding.UTF8.GetBytes("Content example")), ContentEncoding.Binary)
                    }
                });
            var savePath = this._messageRepository.SaveMessage(fs => existedMail.WriteTo(fs));
            var messageId = Path.GetFileName(savePath);

            var response = Get($"/api/messages/{messageId}/raw");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

            var disposition = response.Content.Headers.ContentDisposition;
            Assert.AreEqual(DispositionTypeNames.Attachment, disposition.DispositionType);
            Assert.AreEqual(messageId, disposition.FileName);


            MimeMessage downloadMessage;
            using (var raw = response.Content.ReadAsStreamAsync().Result)
            {
                downloadMessage = MimeMessage.Load(ParserOptions.Default, raw);
            }
            Assert.AreEqual("from@from.com", ((MailboxAddress) downloadMessage.From.First()).Address);
            Assert.AreEqual("to@to.com", ((MailboxAddress)downloadMessage.To.First()).Address);
            Assert.AreEqual("Sample email", downloadMessage.Subject);

            using (var ms = new MemoryStream())
            {
                var bodyContent = (downloadMessage.BodyParts.Single() as MimePart).ContentObject;
                bodyContent.DecodeTo(ms);
                ms.Seek(0, SeekOrigin.Begin);

                Assert.AreEqual("Content example", new StreamReader(ms).ReadToEnd());
            }
        }

        class MessageListResponse
        {
            public MessageListResponse()
            {
                Messages = new List<MimeMessageEntry.RefDto>();
            }

            public int TotalMessageCount { get; set; }
            public List<MimeMessageEntry.RefDto> Messages { get; set; }
        }
    }
}