# MacWidget Third-Party Notices

This file is distributed with MacWidget. It covers third-party software and data that are
included in, installed for, or used by the application. MacWidget itself is not released under
this file's terms.

## Microsoft.Web.WebView2 SDK 1.0.4078.44

Copyright (C) Microsoft Corporation. All rights reserved.

Redistribution and use in source and binary forms, with or without modification, are permitted
provided that the following conditions are met:

- Redistributions of source code must retain the above copyright notice, this list of conditions
  and the following disclaimer.
- Redistributions in binary form must reproduce the above copyright notice, this list of
  conditions and the following disclaimer in the documentation and/or other materials provided
  with the distribution.
- The name of Microsoft Corporation, or the names of its contributors may not be used to endorse
  or promote products derived from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR
IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND
FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR
CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER
IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT
OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

MacWidget uses the Microsoft Edge WebView2 Evergreen Runtime. The installer bundles Microsoft's
bootstrapper only to install that runtime when it is missing; the runtime and its updates are
supplied by Microsoft. Current distribution guidance:
https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution

## MET Norway Locationforecast 2.0 weather data

Weather data is provided by the Norwegian Meteorological Institute (MET Norway) through its
Locationforecast 2.0 API. MacWidget transforms the returned data into its own widget display.
The source data is licensed under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/).

Attribution: "Weather data from MET Norway". This attribution does not imply endorsement by
MET Norway, Yr, or NRK. MacWidget identifies its requests with an application User-Agent and
requests updates at most once per configured city every 15 minutes while that widget is visible.
