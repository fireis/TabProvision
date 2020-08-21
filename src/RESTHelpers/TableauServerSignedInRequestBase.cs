﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Net;

/// <summary>
/// Abstract class for making requests AFTER having logged into the server
/// </summary>
abstract class TableauServerSignedInRequestBase : TableauServerRequestBase
{
    protected readonly TableauServerSignIn _onlineSession;

    public TaskStatusLogs StatusLog
    {
        get
        {
            return _onlineSession.StatusLog;
        }
    }
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="login"></param>
    public TableauServerSignedInRequestBase(TableauServerSignIn login)
    {
        _onlineSession = login;
    }


    /// <summary>
    /// Download a file
    /// </summary>
    /// <param name="urlDownload"></param>
    /// <param name="downloadToDirectory"></param>
    /// <param name="baseFilename"></param>
    /// <param name="downloadTypeMapper"></param>
    /// <returns>The path to the downloaded file</returns>
    protected string DownloadFile(string urlDownload, string downloadToDirectory, string baseFilename, DownloadPayloadTypeHelper downloadTypeMapper)
    {
        //Lets keep track of how long it took
        var startDownload = DateTime.Now;
        string outputPath;
        try
        {
            outputPath =  DownloadFile_inner(urlDownload, downloadToDirectory, baseFilename, downloadTypeMapper);
        }
        catch (Exception exDownload)
        {
            this.StatusLog.AddError("Download failed after " + (DateTime.Now - startDownload).TotalSeconds.ToString("#.#") + " seconds. " + urlDownload);

            var failedDownload = DateTime.Now;
            throw exDownload;
        }

        var finishDownload = DateTime.Now;
        this.StatusLog.AddStatus("Download success duration " + (finishDownload - startDownload).TotalSeconds.ToString("#.#") + " seconds. " + urlDownload, -10);
        return outputPath;
    }

    /// <summary>
    /// Downloads a file
    /// </summary>
    /// <param name="urlDownload"></param>
    /// <param name="downloadToDirectory"></param>
    /// <param name="baseFileName">Filename without extension</param>
    /// <returns>The path to the downloaded file</returns>
    private string DownloadFile_inner(
        string urlDownload,
        string downloadToDirectory,
        string baseFilename,
        DownloadPayloadTypeHelper downloadTypeMapper,
        bool overwriteExistingFile = true)
    {

        //[2016-05-06] Interestingly 'GetFileNameWithoutExtension' does more than remove a ".xxxx" extension; it will also remove a preceding
        //            path (e.g. GetFileNameWithoutExtension('foo/bar.xxx') -> "bar'.  This is undesirable because these characters are valid 
        //            in Tableau Server content names. Since this function is supposed to be called with a 'baseFilename' that DOES NOT have a .xxx
        //            extension, it is safe to remove this call
        //baseFilename =  FileIOHelper.GenerateWindowsSafeFilename(System.IO.Path.GetFileNameWithoutExtension(baseFilename));

        //Strip off an extension if its there
        baseFilename = FileIOHelper.GenerateWindowsSafeFilename(baseFilename);


        var webClient = this.CreateLoggedInWebClient();
        using(webClient)
        { 
            //Choose a temp file name to download to
            var starterName = System.IO.Path.Combine(downloadToDirectory, baseFilename + ".tmp");
            //If the temp file exists, delete it
            if(System.IO.File.Exists(starterName))
            {
                System.IO.File.Delete(starterName);
            }

            _onlineSession.StatusLog.AddStatus("Attempting file download: " + urlDownload, -10);
            webClient.DownloadFile(urlDownload, starterName); //Download the file

            //Look up the correct file extension based on the content type downloaded
            var contentType = webClient.ResponseHeaders["Content-Type"];
            var fileExtension = downloadTypeMapper.GetFileExtension(contentType);
            var finishName = System.IO.Path.Combine(downloadToDirectory, baseFilename + fileExtension);

            //See if a preexisting file is there
            if (System.IO.File.Exists(finishName))
            {
                if(overwriteExistingFile)
                {
                    System.IO.File.Delete(finishName);
                }
                else
                {
                    throw new Exception("1025-1152: File exists already " + finishName);
                }
            }

            //Rename the downloaded file
            System.IO.File.Move(starterName, finishName);
            return finishName;
        }
    }

    /// <summary>
    /// Web client class used for downloads from Tableau Server
    /// </summary>
    /// <returns></returns>
    protected WebClient CreateLoggedInWebClient()
    {
        _onlineSession.StatusLog.AddStatus("Web client being created", -10);

        var webClient = new TableauServerWebClient(); //Create a WebClient object with a large TimeOut value so that larger content can be downloaded
        AppendLoggedInHeadersForRequest(webClient.Headers);
        return webClient;
    }

    /// <summary>
    /// Get a web response back, and parse it as an XML document
    /// </summary>
    /// <param name="url"></param>
    /// <param name="actionDescription"></param>
    /// <param name="protocol"></param>
    /// <param name="requestTimeout"></param>
    /// <returns></returns>
    protected System.Xml.XmlDocument ResourceSafe_PerformWebRequest_GetXmlDocument(string url, string actionDescription, string protocol = "GET", Nullable<int> requestTimeout = null)
    {
        var webRequest = this.CreateLoggedInWebRequest(url, protocol, requestTimeout);
        var response = GetWebReponseLogErrors(webRequest, actionDescription);

        if (response != null)
        {
            using (response)
            {
                System.Xml.XmlDocument xmlDoc = GetWebResponseAsXml(response);
                return xmlDoc;
            }
        }

        return null;
    }

    /// <summary>
    /// Performs a request/response, and makes sure we clean up after the response
    /// </summary>
    /// <param name="url"></param>
    /// <param name="protocol"></param>
    /// <param name="actionaDescription"></param>
    /// <param name="requestTimeout"></param>
    /// <returns></returns>
    protected bool ResourceSafe_PerformWebRequestResponseLogErrors(string url, string actionDescription, string protocol = "GET", Nullable<int> requestTimeout = null)
    {
        var webRequest = this.CreateLoggedInWebRequest(url, protocol, requestTimeout);        
        var response = GetWebReponseLogErrors(webRequest, actionDescription);
        if(response != null)
        {
            response.Dispose();
            return true; //Success
        }

        return false; //Failure, we did not get a response
    }


    /// <summary>
    /// Creates a web request and appends the user credential tokens necessary
    /// </summary>
    /// <param name="url"></param>
    /// <param name="protocol"></param>
    /// <param name="requestTimeout">Useful for specifying timeouts for operations that can take a long time</param>
    /// <returns></returns>
    protected WebRequest CreateLoggedInWebRequest(string url, string protocol = "GET", Nullable<int> requestTimeout = null)
    {
        _onlineSession.StatusLog.AddStatus("Attempt web request: " + url, -10);

        var webRequest = WebRequest.Create(url);
        webRequest.Method = protocol;

        //If an explicit timeout was passed in then use it
        if(requestTimeout != null)
        {
            webRequest.Timeout = requestTimeout.Value;
        }

        AppendLoggedInHeadersForRequest(webRequest.Headers);
        return webRequest;
    }


    /// <summary>
    /// Creates a web request with a MIME payload and send it to server
    /// </summary>
    /// <param name="url"></param>
    /// <param name="protocol">e.g. "PUT" "POST" </param>
    /// <param name="mimeToSend">Mime data we are goign to send</param>
    /// <returns></returns>
    protected WebRequest CreateAndSendMimeLoggedInRequest(string url, string protocol, MimeWriterBase mimeToSend, Nullable<int> requestTimeout = null)
    {
        var webRequest = this.CreateLoggedInWebRequest(url, protocol, requestTimeout); 

        //var uploadChunkAsMime = new OnlineMimeUploadChunk(uploadDataBuffer, numBytes);
        var uploadMimeChunk = mimeToSend.GenerateMimeEncodedChunk();

        webRequest.ContentLength = uploadMimeChunk.Length;
        webRequest.ContentType = "multipart/mixed; boundary=" + mimeToSend.MimeBoundaryMarker;

        //Write out the request
        var requestStream = webRequest.GetRequestStream();
        requestStream.Write(uploadMimeChunk, 0, uploadMimeChunk.Length);

        return webRequest;
    }


    /// <summary>
    /// Adds header information that authenticates the request to Tableau Online
    /// </summary>
    /// <param name="webHeaders"></param>
    private void AppendLoggedInHeadersForRequest(WebHeaderCollection webHeaders)
    {
        webHeaders.Add("X-Tableau-Auth", _onlineSession.LogInAuthToken);
        _onlineSession.StatusLog.AddStatus("Append header X-Tableau-Auth: " + _onlineSession.LogInAuthToken, -20);
    }

    /// <summary>
    /// Get the web response; log any error codes that occur and rethrow the exception.
    /// This allows us to get error log data with detailed information
    /// </summary>
    /// <param name="webRequest"></param>
    /// <returns></returns>
    protected WebResponse GetWebReponseLogErrors(WebRequest webRequest, string description)
    {
        string requestUri = webRequest.RequestUri.ToString();
        try
        {
            return webRequest.GetResponse();
        }
        catch (WebException webException)
        {
            AttemptToLogWebException(webException, description + " (" + requestUri + ") ", this.StatusLog);
            throw webException;
        }
    }


    /// <summary>
    /// Attempt to log any detailed information we find about the failed web request
    /// </summary>
    /// <param name="webException"></param>
    /// <param name="onlineStatusLog"></param>
    private static void AttemptToLogWebException(WebException webException, string description, TaskStatusLogs onlineStatusLog)
    {
        if(onlineStatusLog == null) return; //No logger? nothing to do

        try
        {
            if(string.IsNullOrWhiteSpace(description))
            {
                description = "web request failed";
            }
            string responseText = "";

            //NOTE: In some cases (e.g. time-out) the response may be NULL
            var response = webException.Response;
            if(response != null)
            {
                responseText = GetWebResponseAsText(response);
                response.Close();
            }
            
            //Cannonicalize a blank result...
            if(string.IsNullOrEmpty(responseText))
            {
                responseText = "";
            }

            onlineStatusLog.AddError(description +  ": " + webException.Message + "\r\n" + responseText + "\r\n");
        }
        catch (Exception ex)
        {
            onlineStatusLog.AddError("811-830: Error in web request exception: " + ex.Message);
            return;
        }
    }

}
