﻿using System;
using System.Xml;
using System.Collections.Generic;
using System.Text;
using System.Net;

/// <summary>
/// Manages the signed in session for a Tableau Server site's sign in
/// </summary>
class TableauServerSignIn : TableauServerRequestBase
{
    public enum SignInMode
    {
        UserNameAndPassword,
        AuthToken
    }

    private readonly SignInMode _signInMode;
    private readonly TableauServerUrls _onlineUrls;
    private readonly string _signInClientId;
    private readonly string _signInSecret;
 
    public readonly string SiteUrlSegment;
    private string _signedInCookies;
    private string _signedInAccessToken;
    private string _signedInSiteId;
    private string _signedInUserId;
    public readonly TaskStatusLogs StatusLog;
    private bool _isSignedIn; //True while we are signed in


    /// <summary>
    /// Returns the URL manager
    /// </summary>
    public TableauServerUrls ServerUrls
    {
        get
        {
            return _onlineUrls;
        }
    }

    /// <summary>
    /// TRUE if we are currently signed in to a tableau server
    /// </summary>
    public bool IsSignedIn
    {
        get
        {
            return _isSignedIn;
        }
    }

    /// <summary>
    /// Sign us out
    /// </summary>
    /// <param name="serverUrls"></param>
    public void SignOut(TableauServerUrls serverUrls)
    {
        if(!_isSignedIn)
        {
            StatusLog.AddError("Session not signed in. Sign out aborted");
        }

        //Perform the sign out
        var signOut = new TableauServerSignOut(serverUrls, this);
        signOut.ExecuteRequest();

        _isSignedIn = false;
    }

    /// <summary>
    /// Synchronous call to test and make sure sign in works
    /// </summary>
    /// <param name="url"></param>
    /// <param name="userId"></param>
    /// <param name="userPassword"></param>
    /// <param name="statusLog"></param>
    public static void VerifySignInPossible(string url, string userId, string userPassword, TaskStatusLogs statusLog)
    {
        var urlManager = TableauServerUrls.FromContentUrl(url, TaskMasterOptions.RestApiReponsePageSizeDefault);
        var signIn = new TableauServerSignIn(urlManager, userId, userPassword, statusLog);
        bool success = signIn.Execute();

        if(!success)
        {
            throw new Exception("Failed sign in");
        }
    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="onlineUrls"></param>
    /// <param name="signInClientId">Email or Token name</param>
    /// <param name="signInSecret">Password or Secret Token</param>
    /// <param name="statusLog"></param>
    public TableauServerSignIn(
        TableauServerUrls onlineUrls, 
        string signInClientId, 
        string signInSecret, 
        TaskStatusLogs statusLog,
        SignInMode signInMode = SignInMode.UserNameAndPassword)
    {
        if (statusLog == null) { statusLog = new TaskStatusLogs(); }
        this.StatusLog = statusLog;

        _onlineUrls = onlineUrls;
        _signInClientId = signInClientId;
        _signInSecret = signInSecret;
        _signInMode = signInMode;
        SiteUrlSegment = onlineUrls.SiteUrlSegement;
    }

    public string LogInCookies
    {
        get
        {
            return _signedInCookies;
        }
    }
    public string LogInAuthToken
    {
        get
        {
            return _signedInAccessToken;
        }
    }
    public string SiteId
    {
        get
        {
            return _signedInSiteId;
        }
    }
    public string UserId
    {
        get
        {
            return _signedInUserId;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="serverName"></param>
    public bool Execute()
    {
        var webRequest = WebRequest.Create(_onlineUrls.UrlLogin);
        var sbXml = new StringBuilder();
        var xmlWriter = XmlWriter.Create(sbXml, XmlHelper.XmlSettingsForWebRequests);

        if (_signInMode == SignInMode.UserNameAndPassword)
        {

            xmlWriter.WriteStartElement("tsRequest");
            xmlWriter.WriteStartElement("credentials"); //<credentials>
            xmlWriter.WriteAttributeString("name", _signInClientId);
            xmlWriter.WriteAttributeString("password", _signInSecret);
            xmlWriter.WriteStartElement("site");       //<site>
            xmlWriter.WriteAttributeString("contentUrl", SiteUrlSegment);
            xmlWriter.WriteEndElement();               //</site>
            xmlWriter.WriteEndElement();              //</credentials>

            xmlWriter.WriteEndElement();  //</tsRequest>
        }
        else if(_signInMode == SignInMode.AuthToken)
        {
            xmlWriter.WriteStartElement("tsRequest");
            xmlWriter.WriteStartElement("credentials"); //<credentials>
//            xmlWriter.WriteAttributeString("clientId", _userName);
            xmlWriter.WriteAttributeString("personalAccessTokenName", _signInClientId);
            xmlWriter.WriteAttributeString("personalAccessTokenSecret", _signInSecret);
            xmlWriter.WriteStartElement("site");       //<site>
            xmlWriter.WriteAttributeString("contentUrl", SiteUrlSegment);
            xmlWriter.WriteEndElement();               //</site>
            xmlWriter.WriteEndElement();              //</credentials>

            xmlWriter.WriteEndElement();  //</tsRequest>

        }
        else
        {
            this.StatusLog.AddError("Unknown sign in mechanism");
            throw new Exception("Unknown sign in mechanism");
        }

        xmlWriter.Flush();

        string bodyText = sbXml.ToString();
        //===============================================================================================
        //Make the sign in request, trap and note, and rethrow any errors
        //===============================================================================================
        try
        {
            SendPostContents(webRequest, bodyText);
        }
        catch (Exception exSendRequest)
        {
            this.StatusLog.AddError("Error sending sign in request: " + exSendRequest.ToString());
            throw;
        }


        //===============================================================================================
        //Get the web response, trap and note, and rethrow any errors
        //===============================================================================================
        WebResponse response;
        try
        {
            response = webRequest.GetResponse();
        }
        catch(Exception exResponse)
        {
            this.StatusLog.AddError("Error returned from sign in response: " + exResponse.ToString());
            throw;
        }

        var allHeaders = response.Headers;
        var cookies = allHeaders["Set-Cookie"];
        _signedInCookies = cookies; //Keep any cookies

        //===============================================================================================
        //Get the web response's XML payload, trap and note, and rethrow any errors
        //===============================================================================================
        XmlDocument xmlDoc;
        try
        {
            xmlDoc = GetWebResponseAsXml(response);
        }
        catch (Exception exSignInResponse)
        {
            this.StatusLog.AddError("Error returned from sign in xml response: " + exSignInResponse.ToString());
            throw exSignInResponse;
        }

        var nsManager = XmlHelper.CreateTableauXmlNamespaceManager("iwsOnline");
        var credentialNode = xmlDoc.SelectSingleNode("//iwsOnline:credentials", nsManager);
        var siteNode = xmlDoc.SelectSingleNode("//iwsOnline:site", nsManager);
        _signedInSiteId = siteNode.Attributes["id"].Value;
        _signedInAccessToken = credentialNode.Attributes["token"].Value;

        //Adding the UserId to the log-in return was a feature that was added late in the product cycle.
        //For this reason this code is going to defensively look to see if hte attribute is there
        var userNode = xmlDoc.SelectSingleNode("//iwsOnline:user", nsManager);
        string userId = null;
        if(userNode != null)
        {
            var userIdAttribute =  userNode.Attributes["id"];
            if(userIdAttribute != null)
            {
                userId = userIdAttribute.Value;
            }
            _signedInUserId = userId;
        }

        //Output some status text...
        if(!string.IsNullOrWhiteSpace(userId))
        {
            this.StatusLog.AddStatus("Log-in returned user id: '" + userId + "'", -10);
        }
        else
        {
            this.StatusLog.AddStatus("No User Id returned from log-in request");
            return false;  //Failed sign in
        }

        _isSignedIn = true; //Mark us as signed in
        return true; //Success
    }
}
