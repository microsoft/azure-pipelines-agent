// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Moq;
using System;
using System.IO;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Listener
{
    public sealed class PagingLoggerL0
    {
        private const string LogData = "messagemessagemessagemessagemessagemessagemessagemessageXPLATmessagemessagemessagemessagemessagemessagemessagemessage";
        private const string LogDataWithGroup = @"messagemessagemessagemes
        ##[group]sage
        messagemessagemessagemessage
        ##[endgroup]
        XPLATmessagemessagemessagemessagemessagemessagemessagemessage";
        private const string LogDataWithoutOpenGroup = @"messagemessagemessagemes
        messagemessagemessagemessage
        ##[endgroup]
        XPLATmessagemessagemessagemessagemessagemessagemessagemessage";
        private const string LogDataWithoutCloseGroup = @"messagemessagemessagemes
        ##[group]sage
        messagemessagemessagemessage
        XPLATmessagemessagemessagemessagemessagemessagemessagemessage";
        private const string LogDataUpperCaseGroup = @"messagemessagemessagemes
        ##[GROUP]sage
        messagemessagemessagemessage
        ##[ENDGROUP]
        XPLATmessagemessagemessagemessagemessagemessagemessagemessage";
        private const int PagesToWrite = 2;
        private Mock<IJobServerQueue> _jobServerQueue;

        public PagingLoggerL0()
        {
            _jobServerQueue = new Mock<IJobServerQueue>();
            PagingLogger.PagingFolder = "pages_" + Guid.NewGuid().ToString();
        }

        private void CleanLogFolder()
        {
            using (TestHostContext hc = new TestHostContext(this))
            {
                //clean test data if any old test forgot
                string pagesFolder = Path.Combine(hc.GetDiagDirectory(), PagingLogger.PagingFolder);
                if (Directory.Exists(pagesFolder))
                {
                    Directory.Delete(pagesFolder, true);
                }
            }
        }

        //WriteAndShipLog test will write "PagesToWrite" pages of data,
        //verify file content on the disk and check if API to ship data is invoked
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void WriteAndShipLog()
        {
            CleanLogFolder();

            try
            {
                //Arrange
                using (var hc = new TestHostContext(this))
                using (var pagingLogger = new PagingLogger())
                {
                    hc.SetSingleton<IJobServerQueue>(_jobServerQueue.Object);
                    pagingLogger.Initialize(hc);
                    Guid timeLineId = Guid.NewGuid();
                    Guid timeLineRecordId = Guid.NewGuid();
                    int totalBytes = PagesToWrite * PagingLogger.PageSize;
                    int bytesWritten = 0;
                    int logDataSize = System.Text.Encoding.UTF8.GetByteCount(LogData);
                    _jobServerQueue.Setup(x => x.QueueFileUpload(timeLineId, timeLineRecordId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), true))
                        .Callback((Guid timelineId, Guid timelineRecordId, string type, string name, string path, bool deleteSource) =>
                        {
                            bool fileExists = File.Exists(path);
                            Assert.True(fileExists);

                            using (var freader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read), System.Text.Encoding.UTF8))
                            {
                                string line;
                                while ((line = freader.ReadLine()) != null)
                                {
                                    Assert.True(line.EndsWith(LogData));
                                    bytesWritten += logDataSize;
                                }
                            }
                            File.Delete(path);
                        });

                    //Act
                    int bytesSent = 0;
                    pagingLogger.Setup(timeLineId, timeLineRecordId);
                    while (bytesSent < totalBytes)
                    {
                        pagingLogger.Write(LogData);
                        bytesSent += logDataSize;
                    }
                    pagingLogger.End();

                    //Assert
                    _jobServerQueue.Verify(x => x.QueueFileUpload(timeLineId, timeLineRecordId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), true), Times.AtLeast(PagesToWrite));
                    Assert.Equal(bytesSent, bytesWritten);
                }
            }
            finally
            {
                //cleanup
                CleanLogFolder();
            }
        }

        //Try to ship empty log
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void ShipEmptyLog()
        {
            CleanLogFolder();

            try
            {
                //Arrange
                using (var hc = new TestHostContext(this))
                using (var pagingLogger = new PagingLogger())
                {
                    hc.SetSingleton<IJobServerQueue>(_jobServerQueue.Object);
                    pagingLogger.Initialize(hc);
                    Guid timeLineId = Guid.NewGuid();
                    Guid timeLineRecordId = Guid.NewGuid();
                    _jobServerQueue.Setup(x => x.QueueFileUpload(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), true));

                    //Act
                    pagingLogger.Setup(timeLineId, timeLineRecordId);
                    pagingLogger.End();

                    //Assert
                    _jobServerQueue.Verify(x => x.QueueFileUpload(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), true), Times.Exactly(0));
                }
            }
            finally
            {
                //cleanup
                CleanLogFolder();
            }
        }

        // 
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void CalculateLineNumbers()
        {
            CleanLogFolder();

            try
            {
                //Arrange
                using (var hc = new TestHostContext(this))
                using (var pagingLogger = new PagingLogger())
                {
                    hc.SetSingleton<IJobServerQueue>(_jobServerQueue.Object);
                    pagingLogger.Initialize(hc);
                    Guid timeLineId = Guid.NewGuid();
                    Guid timeLineRecordId = Guid.NewGuid();
                    int totalBytes = PagesToWrite * PagingLogger.PageSize;
                    int logDataSize = System.Text.Encoding.UTF8.GetByteCount(LogData);
                    
                    //Act
                    int bytesSent = 0;
                    int expectedLines = 0;
                    pagingLogger.Setup(timeLineId, timeLineRecordId);

                    while (bytesSent < totalBytes)
                    {
                        pagingLogger.Write(LogData);
                        bytesSent += logDataSize;
                        expectedLines++;
                    }
                    pagingLogger.End();

                    //Assert
                    _jobServerQueue.Verify(x => x.QueueFileUpload(timeLineId, timeLineRecordId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), true), Times.AtLeast(PagesToWrite));
                    Assert.Equal(pagingLogger.TotalLines, expectedLines);
                }
            }
            finally
            {
                //cleanup
                CleanLogFolder();
            }
        }


        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void CalculateLineNumbersWithGroupTag()
        {
            CleanLogFolder();

            try
            {
                //Arrange
                using (var hc = new TestHostContext(this))
                using (var pagingLogger = new PagingLogger())
                {
                    hc.SetSingleton<IJobServerQueue>(_jobServerQueue.Object);
                    pagingLogger.Initialize(hc);
                    Guid timeLineId = Guid.NewGuid();
                    Guid timeLineRecordId = Guid.NewGuid();
                    int totalBytes = PagesToWrite * PagingLogger.PageSize;
                    int logDataSize = System.Text.Encoding.UTF8.GetByteCount(LogDataWithGroup);
               
                    //Act
                    int bytesSent = 0;
                    int expectedLines = 0;
                    // -1 because ##[endgroup] should be ignored since it's not shown in UI and not counted in line numbers
                    int lineCnt = LogDataWithGroup.Split('\n').Length - 1;
                    pagingLogger.Setup(timeLineId, timeLineRecordId);
                    while (bytesSent < totalBytes)
                    {
                        pagingLogger.Write(LogDataWithGroup);
                        bytesSent += logDataSize;
                        expectedLines += lineCnt;
                    }
                    pagingLogger.End();

                    //Assert
                    _jobServerQueue.Verify(x => x.QueueFileUpload(timeLineId, timeLineRecordId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), true), Times.AtLeast(PagesToWrite));
                    Assert.Equal(pagingLogger.TotalLines, expectedLines);
                }
            }
            finally
            {
                //cleanup
                CleanLogFolder();
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void CalculateLineNumbersWithoutOpenGroupTag()
        {
            CleanLogFolder();

            try
            {
                //Arrange
                using (var hc = new TestHostContext(this))
                using (var pagingLogger = new PagingLogger())
                {
                    hc.SetSingleton<IJobServerQueue>(_jobServerQueue.Object);
                    pagingLogger.Initialize(hc);
                    Guid timeLineId = Guid.NewGuid();
                    Guid timeLineRecordId = Guid.NewGuid();
                    int totalBytes = PagesToWrite * PagingLogger.PageSize;
                    int logDataSize = System.Text.Encoding.UTF8.GetByteCount(LogDataWithoutOpenGroup);
               
                    //Act
                    int bytesSent = 0;
                    int expectedLines = 0;
                    // ##[endgroup] should be transform as empty space line, so all lines should count
                    int lineCnt = LogDataWithoutOpenGroup.Split('\n').Length;
                    pagingLogger.Setup(timeLineId, timeLineRecordId);
                    while (bytesSent < totalBytes)
                    {
                        pagingLogger.Write(LogDataWithoutOpenGroup);
                        bytesSent += logDataSize;
                        expectedLines += lineCnt;
                    }
                    pagingLogger.End();

                    //Assert
                    _jobServerQueue.Verify(x => x.QueueFileUpload(timeLineId, timeLineRecordId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), true), Times.AtLeast(PagesToWrite));
                    Assert.Equal(pagingLogger.TotalLines, expectedLines);
                }
            }
            finally
            {
                //cleanup
                CleanLogFolder();
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void CalculateLineNumbersWithoutCloseGroupTag()
        {
            CleanLogFolder();

            try
            {
                //Arrange
                using (var hc = new TestHostContext(this))
                using (var pagingLogger = new PagingLogger())
                {
                    hc.SetSingleton<IJobServerQueue>(_jobServerQueue.Object);
                    pagingLogger.Initialize(hc);
                    Guid timeLineId = Guid.NewGuid();
                    Guid timeLineRecordId = Guid.NewGuid();
                    int totalBytes = PagesToWrite * PagingLogger.PageSize;
                    int logDataSize = System.Text.Encoding.UTF8.GetByteCount(LogDataWithoutCloseGroup);
               
                    //Act
                    int bytesSent = 0;
                    int expectedLines = 0;
                    // ##[group] should be show as grope name and the rest will be the same, so all lines should count
                    int lineCnt = LogDataWithoutCloseGroup.Split('\n').Length;
                    pagingLogger.Setup(timeLineId, timeLineRecordId);
                    while (bytesSent < totalBytes)
                    {
                        pagingLogger.Write(LogDataWithoutCloseGroup);
                        bytesSent += logDataSize;
                        expectedLines += lineCnt;
                    }
                    pagingLogger.End();

                    //Assert
                    _jobServerQueue.Verify(x => x.QueueFileUpload(timeLineId, timeLineRecordId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), true), Times.AtLeast(PagesToWrite));
                    Assert.Equal(pagingLogger.TotalLines, expectedLines);
                }
            }
            finally
            {
                //cleanup
                CleanLogFolder();
            }
        }
    
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void CalculateLineNumbersWithUpperCaseGroupTag()
        {
            CleanLogFolder();

            try
            {
                //Arrange
                using (var hc = new TestHostContext(this))
                using (var pagingLogger = new PagingLogger())
                {
                    hc.SetSingleton<IJobServerQueue>(_jobServerQueue.Object);
                    pagingLogger.Initialize(hc);
                    Guid timeLineId = Guid.NewGuid();
                    Guid timeLineRecordId = Guid.NewGuid();
                    int totalBytes = PagesToWrite * PagingLogger.PageSize;
                    int logDataSize = System.Text.Encoding.UTF8.GetByteCount(LogDataUpperCaseGroup);
               
                    //Act
                    int bytesSent = 0;
                    int expectedLines = 0;
                    // -1 because ##[endgroup] should be ignored since it's not shown in UI and not counted in line numbers
                    int lineCnt = LogDataUpperCaseGroup.Split('\n').Length - 1;
                    pagingLogger.Setup(timeLineId, timeLineRecordId);
                    while (bytesSent < totalBytes)
                    {
                        pagingLogger.Write(LogDataUpperCaseGroup);
                        bytesSent += logDataSize;
                        expectedLines += lineCnt;
                    }
                    pagingLogger.End();

                    //Assert
                    _jobServerQueue.Verify(x => x.QueueFileUpload(timeLineId, timeLineRecordId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), true), Times.AtLeast(PagesToWrite));
                    Assert.Equal(pagingLogger.TotalLines, expectedLines);
                }
            }
            finally
            {
                //cleanup
                CleanLogFolder();
            }
        }
    }
}