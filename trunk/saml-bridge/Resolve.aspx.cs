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
using System.Collections;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Web;
using System.Web.SessionState;
using System.Web.UI;
using System.Web.UI.WebControls;
using System.Web.UI.HtmlControls;
using System.IO;
using System.Xml;
using System.Security;
using System.Security.Principal;

namespace SAMLServices
{
	/// <summary>
	/// Summary description for ResolveArt.
	/// This page is called from the GSA to resolve an artifact, and obtain a user's ID.
	/// It expects to receive a SAML message with the artifact and time stamp.
	/// </summary>
    public partial class Resolve : System.Web.UI.Page
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
		/// Method to extract the core information from the SAML message,
		/// specifically the artifact and the SAML ID of the request.
		/// </summary>
		/// <returns>Two element String array, the first is the artifact, the second is the request ID</returns>
		String [] ExtractInfo()
		{
			String [] result = new String[3];
			Common.debug("inside ResolveArt::ExtractInfo");
			// Get the SAML message from the request, in the form of a string
			String req = Common.ReadRequest(Request);
			Common.debug("request from GSA=" + req);

			// Find the artifact node and obtain the InnerText
			XmlNode node = Common.FindOnly(req, Common.ARTIFACT);
			result[0] = node.InnerText;
            Common.debug("Artifiact Id = " + result[0]);

			// Find the SAML request ID in an XML attribute.
			//  This is needed for the response for the GSA
			//  to understand how to match it up with its request
			node  = Common.FindOnly(req, Common.ArtifactResolve);
			result[1] = node.Attributes[Common.ID].Value;
            Common.debug("SAML Resolve Request Id = " + result[1]);

            //Find the Issuer details. 
            //These need to be passed back as AudienceRestriction in the Assertion Conditions
            node = Common.FindOnly(req, Common.ISSUER);
            result[2] = node.InnerText;
            Common.debug("Resolve Requester = " + result[2]);

            Common.debug("exit ResolveArt::ExtractInfo");
			return result;
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
            String recipientGsa = Common.GSAArtifactConsumer;
            if (!recipientGsa.StartsWith("http"))
            {
                recipientGsa = "http://" + Request.Headers["Host"] + recipientGsa;
            }

			Common.debug("inside BuildResponse");
			String req = Common.AuthNResponseTemplate;
			req = req.Replace("%REQID", responseTo);
            DateTime currentTimeStamp = DateTime.Now;
			req = req.Replace("%INSTANT", Common.FormatInvariantTime(currentTimeStamp));
            req = req.Replace("%NOT_ON_OR_AFTER", Common.FormatInvariantTime(currentTimeStamp.AddSeconds(5)));

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
