﻿/*
 *
 * (c) Copyright Ascensio System Limited 2010-2021
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 * http://www.apache.org/licenses/LICENSE-2.0
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
*/


using System.Collections.Generic;

using ASC.Web.Core;

namespace ASC.Web.Studio
{
    public partial class Warmup : WarmupPage
    {
        protected override List<string> Pages
        {
            get
            {
                return new List<string>(10)
                {
                    "Management.aspx?type=1",
                    "Management.aspx?type=2",
                    "Management.aspx?type=3",
                    "Management.aspx?type=4",
                    "Management.aspx?type=5",
                    "Management.aspx?type=6",
                    "Management.aspx?type=7",
                    "Management.aspx?type=10",
                    "Management.aspx?type=11",
                    "Management.aspx?type=15",
                };
            }
        }

        protected override List<string> Exclude
        {
            get
            {
                return new List<string>(5)
                {
                    "Auth.aspx",
                    "403.aspx",
                    "404.aspx",
                    "500.aspx",
                    "PaymentRequired.aspx",
                    "ServerError.aspx",
                    "Tariffs.aspx",
                    "Terms.aspx",
                    "Wizard.aspx"
                };
            }
        }
    }
}