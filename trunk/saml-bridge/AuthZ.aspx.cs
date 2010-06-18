/*
 * Copyright (C) 2006 Google Inc.
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
using System.IO;
using System.Drawing;
using System.Web;
using System.Xml;
using System.Web.SessionState;
using System.Web.UI;
using System.Web.UI.WebControls;
using System.Web.UI.HtmlControls;
using System.Security.Principal;
using System.Net;
using System.Security;
using System.Collections.Generic;

namespace SAMLServices
{
	/// <summary>
	/// Summary description for AuthZ.
	/// This page is called from the GSA to detemine user access to a given URL.
	/// It expects to receive a SAML message with the URL and user ID.
	/// </summary>
	public partial class AuthZ : System.Web.UI.Page
	{
		private void Page_Load(object sender, System.EventArgs e)
		{
			Common.debug("inside Authz::Page_Load");
			// Extract the URL and user ID from the SAML message
			Object[] resp = ExtractRequest();
			if (resp == null)
			{
				Common.printHeader(Response);
				Response.Write("Application Pool Identity  = "  + WindowsIdentity.GetCurrent().Name);
				Response.Write("<br>");
				Response.Write("Your Windows account = " + Page.User.Identity.Name);
				Common.printFooter(Response);
				return;
			}
			// Check authorization and respond
			Authorize((string) resp[0], (List<string>) resp[1]);
		}

		/// <summary>
		///  Method to extract the core information from the SAML message,
		///  specifically the URL and user ID of the request.
		/// </summary>
		/// <returns>Two element string array, the first is subject, the second is URL List</returns>
		Object[] ExtractRequest()
		{
			Common.debug("inside Authz::ExtractRequest");
			Object[] resp = new Object[2];

			// Get the SAML message (in String form) from the HTTP request
			String req = Common.ReadRequest(Request);
			Common.debug("The AuthZ request is: " + req);
			if (req == null || "".Equals(req))
			{
				return null;
			}
			// Create an XMLDocument from the request
			XmlDocument doc = new XmlDocument();
			doc.InnerXml = req;
			// Decode the XML string
			req = HttpUtility.HtmlDecode(req);
			// Find the <saml:Subject/> element
			XmlNodeList list = doc.GetElementsByTagName("Subject", "urn:oasis:names:tc:SAML:2.0:assertion");
			if (list == null || list.Count == 0)
			{
				Common.error("No saml:Subject node");
				return (null);
			}
			XmlNode sub  = list.Item(0);
			if (sub == null)
			{
				Common.error("No child node of saml:Subject node");
				return (null);
			}
			// The User ID (distinguished name) is in the Subject element,
			//  so chop up the InnerXml string to obtain it.
			resp[0] = sub.ChildNodes[0].InnerText;
			Common.debug("subject=" + resp[0]);
			// The URL is in the Resource attribute of the AuthzDecisionQuery node
			XmlNodeList urlNodeList = Common.FindAllElements(doc, "AuthzDecisionQuery");
            List<string> urls = new List<string>();
            foreach (XmlNode urlNode in urlNodeList)
            {
                string url = urlNode.Attributes["Resource"].Value;
                Common.debug("URL To authorize: " + url);
                if (url == null || url.Trim().Length <= 0)
                {
                    Common.error("Resource URL is null or empty");
                }
                else
                {
                    urls.Add(url);
                }
            }

			resp[1]= urls;
			Common.debug("AuthZ batch size = " + urls.Count);
			return resp;
		}

		/// <summary>
		///
		///Method to determin user's privileges to access the given URL.
		///AuthZ decision is determined, the responding SAML AuthZ message is built and sent.
		/// </summary>
		/// <param name="subject"></param>
		/// <param name="url"></param>
		void Authorize(String subject, String url)
		{
			Common.debug("inside Authz::Authorize");
			// Build the base level AuthZ response from the XML template, replacing
			//  certain entities/attributes as appropriate
			String req = Common.AuthZResponseTemplate;
			req = req.Replace("%RESPONSE_ID", Common.GenerateRandomString());
			req = req.Replace("%ASSERTION_ID", Common.GenerateRandomString());
			req = req.Replace("%INSTANT", Common.FormatInvariantTime(DateTime.Now));
			req = req.Replace("%STATUS", "Success");
			req = req.Replace("%ISSUER", SecurityElement.Escape(Server.MachineName));
			req = req.Replace("%SUBJECT", SecurityElement.Escape(subject));
			req = req.Replace("%RESOURCE",SecurityElement.Escape( url));
			// Initialize an AuthZ object
			IAuthz authz = AAFactory.getAuthz(this);
			subject = subject.Trim();
			// Get the user's permissions to this object (url).
			String sStatus = authz.GetPermission(url, subject);
			// Insert the authorization decision into the response
			req = req.Replace("%DECISION", sStatus);
			Common.debug("Authorization response: " + req);
			Response.Write(req);			
		}

        /// <summary>
        ///
        ///Method to determin user's privileges to access the given URL.
        ///AuthZ decision is determined, the responding SAML AuthZ message is built and sent.
        /// </summary>
        /// <param name="subject"></param>
        /// <param name="url"></param>
        void Authorize(String subject, List<string> urls)
        {
            Common.debug("inside Authz::Authorize::Checking AuthZ for each url");
            Response.ContentType = "text/xml";
            Response.Write(Common.AuthZResponseSopaEnvelopeStart);

            //TODO - Need to investigate further. Sequential processing can be time consuming
            //and can potentially cause GSA AuthZ request timeouts (timeout can be increased)
            //This can be done using a multithreaded approach
            //But use of ThreadPool is discouraged in ASP.NET as it might lead to 
            //queuing of requests if many threads are used up by a few requests
            //Spawning new threads manually can be risky as it can lead 
            //to creation of too many threads, 
            //delayed response and sometimes unpredicatble behavior (unmanaged threads)
            foreach (string url in urls)
            {
                Common.debug("inside Authz::Authorize::Checking AuthZ for url: " + url);
                Authorize(subject, url);
            }

            Response.Write(Common.AuthZResponseSopaEnvelopeEnd);
            Common.debug("inside Authz::Authorize::Authz of all Urls completed");
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
