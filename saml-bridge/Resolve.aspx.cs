/*
 * Copyright (C) 2006-2010 Google Inc.
 * 
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy of
 * the License at
 * 
 * http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations under
 * the License.
 */

using System;
using System.Xml;
using System.Security;

namespace SAMLServices
{
	/// <summary>
	/// Summary description for ResolveArt.
	/// This page is called from the GSA to resolve an artifact, and obtain a user's ID.
	/// It expects to receive a SAML message with the artifact and time stamp.
	/// </summary>
    public partial class Resolve : AuthenticationPage 
	{
		private void Page_Load(object sender, System.EventArgs e)
		{
			Common.debug("inside ResolveArt::Page_Load");
			// Get the artifact and SAML request ID
			String [] info = ExtractInfo();
			if (info == null)
			{
				Common.error("Handshake error: Resolve Message is wrong");
				return;
			}
			// Using the artifact, get the user ID
            SamlArtifactCacheEntry samlArtifactCacheEntry = GetSamlArtifactCacheEntry(info[0]);
			if (samlArtifactCacheEntry == null || samlArtifactCacheEntry.Subject == null ||  samlArtifactCacheEntry.Subject.Trim().Length==0)
			{
				Common.error("Handshake error: valid user identity not found!");
				return;
			}
			// Construct the SAML response to the GSA
			String res = BuildResponse(info[1], samlArtifactCacheEntry, info[2]);
			Common.debug("Authentication response=" + res);
			Response.Write (res);
		}
		/// <summary>
		/// Method to obtain cached user ID and AuthN request ID from the given artifact
		/// </summary>
		/// <param name="artifact"></param>
        /// <returns>SamlArtifactCacheEntry</returns>
        SamlArtifactCacheEntry GetSamlArtifactCacheEntry(String artifact)
		{
			Common.debug("inside GetSubject");
			// User ID is stored in an application variable (e.g. Artifact_someLongString = username@domain)
			String artifactKey = Common.ARTIFACT + "_" + artifact;
			// Get the user ID from the application space.  If it doesn't exist, 
			//  this might be a spoof attempt with an invalid artifact
			SamlArtifactCacheEntry samlArtifactCacheEntry = (SamlArtifactCacheEntry) Application.Get(artifactKey);
            //Artifact can be used only once as per SAML standard. 
			if (samlArtifactCacheEntry != null )
			{
                Application.Remove(artifactKey);
				return samlArtifactCacheEntry;
			}
			else
			{
				Common.error("Artifact ID not found!");
				return null;
			}
		}

		/// <summary>
		/// Method to construct the SAML AuthN response using a template, replacing
		/// certain string placeholders with appropriate values.
		/// </summary>
		/// <param name="responseTo"></param>
        /// <param name="samlArtifactCacheEntry"></param>
		/// <returns>Artifact Resolution Response XML</returns>
        String BuildResponse(String responseTo, SamlArtifactCacheEntry samlArtifactCacheEntry, String audienceRestriction)
		{
            String recipientGsa = samlArtifactCacheEntry.SamlAssertionConsumerURL;
            if (!recipientGsa.StartsWith("http"))
            {
                recipientGsa = "http://" + Request.Headers["Host"] + recipientGsa;
            }

			Common.debug("inside BuildResponse");
			String req = Common.AuthNResponseTemplate;
			req = req.Replace("%REQID", responseTo);
            DateTime currentTimeStamp = DateTime.Now;
			req = req.Replace("%INSTANT", Common.FormatInvariantTime(currentTimeStamp.AddMinutes(-1)));
            req = req.Replace("%NOT_ON_OR_AFTER", Common.FormatInvariantTime(currentTimeStamp.AddSeconds(Common.iTrustDuration)));

            String idpEntityId;
            if(Common.IDPEntityId.Length>0)
            {
                idpEntityId = Common.IDPEntityId;
            }
            else
            {
                Common.debug("IDP Entity ID is not set in config. Using machine name as default");
                idpEntityId = SecurityElement.Escape(Server.MachineName.Trim());
            }
            Common.debug("IDP Entity ID used as Issuer is: " + idpEntityId);
            req = req.Replace("%ISSUER", idpEntityId);

            req = req.Replace("%MESSAGE_ID", Common.GenerateRandomString());
			req = req.Replace("%ASSERTION_ID", Common.GenerateRandomString());
            req = req.Replace("%SESSION_INDEX", Common.GenerateRandomString());
			req = req.Replace("%STATUS", "Success");
			req = req.Replace("%CLASS", "InternetProtocol");
            req = req.Replace("%SUBJECT", SecurityElement.Escape(samlArtifactCacheEntry.Subject));
            req = req.Replace("%AUTHN_REQUEST_ID", SecurityElement.Escape(samlArtifactCacheEntry.AuthNRequestId));
            req = req.Replace("%RECIPIENT", recipientGsa);
            req = req.Replace("%AUDIENCE_RESTRICTION", audienceRestriction);
			XmlDocument doc = new XmlDocument();
			doc.InnerXml = req;
			Common.debug("exit BuildResponse");
			return doc.InnerXml;
		}

		#region Web Form Designer generated code
		override protected void OnInit(EventArgs e)
		{
			//
			// CODEGEN: This call is required by the ASP.NET Web Form Designer.
			//
			InitializeComponent();
			base.OnInit(e);
		}
		
		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{    
			this.Load += new System.EventHandler(this.Page_Load);
		}
		#endregion
	}
}
