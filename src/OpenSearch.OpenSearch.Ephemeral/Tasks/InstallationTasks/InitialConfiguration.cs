/* SPDX-License-Identifier: Apache-2.0
*
* The OpenSearch Contributors require contributions made to
* this file be licensed under the Apache-2.0 license or a
* compatible open source license.
*
* Modifications Copyright OpenSearch Contributors. See
* GitHub history for details.
*
*  Licensed to Elasticsearch B.V. under one or more contributor
*  license agreements. See the NOTICE file distributed with
*  this work for additional information regarding copyright
*  ownership. Elasticsearch B.V. licenses this file to you under
*  the Apache License, Version 2.0 (the "License"); you may
*  not use this file except in compliance with the License.
*  You may obtain a copy of the License at
*
* 	http://www.apache.org/licenses/LICENSE-2.0
*
*  Unless required by applicable law or agreed to in writing,
*  software distributed under the License is distributed on an
*  "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
*  KIND, either express or implied.  See the License for the
*  specific language governing permissions and limitations
*  under the License.
*/

using System;
using System.IO;
using OpenSearch.OpenSearch.Managed.ConsoleWriters;
using OpenSearch.Stack.ArtifactsApi;

namespace OpenSearch.OpenSearch.Ephemeral.Tasks.InstallationTasks
{
	public class InitialConfiguration : ClusterComposeTask
	{
		public override void Run(IEphemeralCluster<EphemeralClusterConfiguration> cluster)
		{
			if (cluster.CachingAndCachedHomeExists()) return;

			if (cluster.ClusterConfiguration.Artifact.ServerType == ServerType.ElasticSearch)
			{
				cluster.Writer?.WriteDiagnostic($"{{{nameof(Run)}}} skipping for ElasticSearch");
				return;
			}

			var fs = cluster.FileSystem;
			var script = Path.Combine(fs.OpenSearchHome, "server-initial-config.sh");

			if (cluster.ClusterConfiguration.Artifact.ServerType == ServerType.OpenSearch)
				File.WriteAllText(script, InitialConfigurationOpenSearch.GetConfigurationScript(cluster.ClusterConfiguration.Version));
			if (cluster.ClusterConfiguration.Artifact.ServerType == ServerType.OpenDistro)
				File.WriteAllText(script, InitialConfigurationOpenDistro.GetConfigurationScript());

			cluster.Writer?.WriteDiagnostic($"{{{nameof(Run)}}} going to run [server-initial-config.sh]");

			ExecuteBinary(
				cluster.ClusterConfiguration,
				cluster.Writer,
				"/bin/bash",
				"run initial cluster configuration",
				script);

			if (!cluster.ClusterConfiguration.EnableSsl)
			{
				if (cluster.ClusterConfiguration.Artifact.ServerType == ServerType.OpenSearch)
					File.AppendAllText(Path.Combine(fs.OpenSearchHome, "config", "opensearch.yml"), "plugins.security.disabled: true");
				if (cluster.ClusterConfiguration.Artifact.ServerType == ServerType.OpenDistro)
					File.AppendAllText(Path.Combine(fs.OpenSearchHome, "config", "elasticsearch.yml"), "opendistro_security.disabled: true");
			}

			if (cluster.ClusterConfiguration.Artifact.ServerType == ServerType.ElasticSearch && cluster.ClusterConfiguration.EnableSsl)
				throw new NotImplementedException("ElasticSearch with SSL is not supported");
		}
	}
}
