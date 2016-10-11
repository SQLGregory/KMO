﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using smo = Microsoft.SqlServer.Management.Smo;

namespace KMO
{
    public static class KServer
    {

        #region General informations
        /// <summary>
        /// Get the machine type : this server is a physical server or a Virtual Machine ?
        /// </summary>
        /// <param name="s">your smo server</param>
        /// <returns>Physical, virtual or unknown</returns>
        public static string MachineType(this smo.Server s)
        {
            string serverType = "Unknown";
            smo.Database d = s.Databases["master"];
            DataTable dt = d.ExecuteWithResults("SELECT virtual_machine_type_desc FROM sys.dm_os_sys_info").Tables[0];
            if (dt != null && dt.Rows.Count > 0)
            {
                string virtualType = dt.Rows[0]["virtual_machine_type_desc"].ToString();
                switch (virtualType)
                {
                    case "NONE":
                        serverType = "Physical server";
                        break;
                    case "HYPERVISOR":
                        serverType = "Virtual server";
                        break;
                }
            }
            return serverType;
        }

        /// <summary>
        /// When Sql server service restarts, TempDB is recreated. So tempdb create date can be used to know when the server restarted.
        /// </summary>
        /// <param name="s">your smo server</param>
        /// <returns>the datetime when sql service restart</returns>
        public static DateTime LastRestart(this smo.Server s)
        {
            return s.Databases["tempdb"].CreateDate;
        }

        /// <summary>
        /// Get services account with status
        /// </summary>
        /// <param name="s">your smo server</param>
        /// <returns>a datatable with service name + service informations</returns>
        public static DataTable ServiceStatus(this smo.Server s)
        {
            string sql = @"SELECT servicename
    , service_account + ' (' + status_desc + ')' AS serviceInfo
FROM sys.dm_server_services";
            smo.Database d = s.Databases["master"];
            return d.ExecuteWithResults(sql).Tables[0];
        }
        #endregion

        #region SQL Server Versions
        /// <summary>
        /// Get Sql server version name from major and minor version
        /// </summary>
        /// <param name="s">your smo server object</param>
        /// <returns>a string</returns>
        public static string VersionName(this smo.Server s)
        {
            string version;
            switch (s.VersionMajor.ToString() + "." + s.VersionMinor.ToString())
            {
                case "13.0":
                    version = "Sql Server 2016";
                    break;
                case "12.0":
                    version = "Sql Server 2014";
                    break;
                case "11.0":
                    version = "Sql Server 2012";
                    break;
                case "10.50":
                    version = "Sql Server 2008 R2";
                    break;
                case "10.0":
                    version = "Sql Server 2008";
                    break;
                case "9.0":
                    version = "Sql Server 2005";
                    break;
                case "8.0":
                    version = "Sql Server 2000";
                    break;
                case "7.0":
                    version = "Sql Server 7";
                    break;
                default:
                    version = "Unknown edition";
                    break;
            }
            return version;
        }

        /// <summary>
        /// Get Sql server full version name from major and minor version, productLevel, Edition and version
        /// </summary>
        /// <param name="s">your smo server object</param>
        /// <returns>a string</returns>
        public static string VersionFull(this smo.Server s)
        {
            return string.Format("{0} {1} {2} ({3})", s.VersionName(), s.ProductLevel, s.Edition, s.Version);
        }

        #endregion

        #region Monitoring
        /// <summary>
        /// Get informations about actives session. Locks, performances issues, percent complete, execution plans, etc...
        /// </summary>
        /// <param name="s">Your smo Server</param>
        /// <param name="WithSystemSession">True if you want to select system session. False if you only need user sessions</param>
        /// <param name="WithQueryPlan">True if you want to fill the last column with query plan (xml format). False without execution plan and better performances</param>
        /// <returns>The result of the query in a DataTable</returns>
        public static DataTable GetLiveSession(this smo.Server s, bool WithSystemSession = false, bool WithQueryPlan = false)
        {
            smo.Database d = s.Databases["master"];
            string _sql = @"SELECT CAST(qe.session_id AS VARCHAR) AS [Kill]
    , CASE WHEN CAST(blocking_session_id AS VARCHAR) = '0' THEN '' ELSE CAST(blocking_session_id AS VARCHAR) END AS [Blocking_Session_Info]
	, s.login_name AS [Login]
	, s.host_name AS [Host_Name]
	, s.program_name AS [Program_Name]
	, s.client_interface_name AS [Client_Interface_Name]
	, db_name(qe.Database_id) AS [Database]
	, qe.logical_reads AS [Logical_Read]
	, qe.cpu_time AS [CPU_Time]
	, DATEDIFF(MINUTE, start_time, getdate()) AS [Duration]
	, command AS [Command]
	, qe.status AS [Status]
	, percent_complete AS [Percent_Complete]
	, start_time AS [Start_Time]
    , qe.open_transaction_count AS [Open_Transaction_Count]
    , a.text AS [Query]
    , {0} AS [Execution_Plan]
FROM sys.dm_exec_requests qe (NOLOCK)
	INNER JOIN sys.dm_exec_sessions s (NOLOCK) on qe.session_id = s.session_id 
	LEFT JOIN (select sqe.session_id
					, st.text
				FROM sys.dm_exec_requests sqe (NOLOCK)
					CROSS APPLY sys.dm_exec_sql_text(sqe.sql_handle) st) a ON qe.session_id = a.session_id
{1}
WHERE qe.session_id != @@SPID
	{2}
ORDER BY blocking_session_id DESC
	, Duration DESC
	, [CPU_Time] DESC";
            string _sql0 = "''";
            string _sql1 = string.Empty;
            string _sql2 = string.Empty;
            if (WithQueryPlan)
            {
                _sql0 = "b.query_plan";
                _sql1 = @"	LEFT JOIN (select sqe.session_id
					, ph.query_plan
				FROM sys.dm_exec_requests sqe (NOLOCK)
					CROSS APPLY sys.dm_exec_query_plan(sqe.plan_handle) ph) b ON qe.session_id = b.session_id";
            }
            if (!WithSystemSession)
            {
                _sql2 = "AND s.is_user_process = 1";
            }
            _sql = string.Format(_sql, _sql0, _sql1, _sql2);
            return d.ExecuteWithResults(_sql).Tables[0];
        }

        /// <summary>
        /// Get informations about all sessions on a server
        /// </summary>
        /// <param name="s">Your smo Server</param>
        /// <returns>The result of the query in a DataTable</returns>
        public static DataTable GetWho(this smo.Server s)
        {
            smo.Database d = s.Databases["master"];
            return d.ExecuteWithResults(@"SELECT sp.spid
	, sp.blocked AS Blocked
	, d.name as [Database]
	, RTRIM(sp.hostname) AS [Hostname]
	, RTRIM(sp.program_name) AS [Program Name]
	, RTRIM(sp.loginame) AS [Login Name]
	, RTRIM(sp.nt_domain) AS [NT Domain]
	, RTRIM(sp.nt_username) AS [NT Username]
    , ec.auth_scheme AS [Auth protocol]
	, sp.waittime AS [Wait Time]
	, RTRIM(sp.lastwaittype) AS [Last Wait Type]
	, sp.cpu as [CPU]
	, sp.physical_io AS [Physical IO]
	, sp.memusage AS [Memory]
	, sp.login_time AS [Login Time]
	, sp.last_batch AS [Last Batch]
	, sp.ecid AS [Execution Context ID]
	, sp.open_tran AS [Open Tran]
	, RTRIM(sp.status) AS [Status]
	, sp.hostprocess AS [Host Process]
	, RTRIM(sp.cmd) AS [Command Type]
	, sp.net_address AS [NET Adress]
	, RTRIM(sp.net_library) AS [NET Library]
	, sp2.spid AS [Blocking]
FROM master.dbo.sysprocesses sp
	LEFT JOIN master.dbo.sysdatabases d ON sp.dbid = d.dbid
	LEFT JOIN master.dbo.sysusers u ON sp.uid = u.uid
	LEFT JOIN master.dbo.sysprocesses sp2 ON sp.spid = sp2.blocked
    LEFT JOIN sys.dm_exec_connections ec ON sp.spid = ec.session_id AND ec.parent_connection_id IS NULL
ORDER BY sp.blocked
    , d.name
    , sp.cpu DESC").Tables[0];
        }

        #endregion

        #region Backups
        /// <summary>
        /// For each DB, get the recovery model, the last backup full and the last restore
        /// </summary>
        /// <param name="s">your smo server</param>
        /// <returns>a DataTable with 1 datatable containing the result of the query</returns>
        public static DataTable GetBackupHistory(this smo.Server s)
        {
            smo.Database d = s.Databases["msdb"];
            return d.ExecuteWithResults(@"SELECT sdb.name AS DatabaseName
    , MAX(sdb.recovery_model_desc) as recoveryModel
    , MAX(bus.backup_finish_date) AS LastBackUpTime
	, MAX(rh.restore_date) as LastRestoreTime
FROM sys.databases sdb (NOLOCK)
    LEFT OUTER JOIN msdb.dbo.backupset bus (NOLOCK) ON bus.database_name = sdb.name 
		AND COALESCE(bus.is_snapshot, 0) != 1 
		AND COALESCE(bus.type, 'D') = 'D' 
    LEFT join msdb.dbo.restorehistory rh (NOLOCK) on rh.destination_database_name = sdb.Name
GROUP BY sdb.name
ORDER BY sdb.name").Tables[0];
        }
        #endregion

        #region Logins and Security
        /// <summary>
        /// Get all login with sysadmin rights
        /// </summary>
        /// <param name="s">your smo server</param>
        /// <returns>the result of the query</returns>
        public static DataTable GetWhoIsSa(this smo.Server s)
        {
            smo.Database d = s.Databases["master"];
            return d.ExecuteWithResults(@"SELECT mem.name AS [Login Name]
	, mem.type_desc AS [Login Type]
	, mem.create_date AS [Create Date]
	, mem.modify_date AS [Modify Date]
	, mem.is_disabled AS [Is Disabled]
FROM sys.server_role_members AS srm (NOLOCK)
	INNER JOIN sys.server_principals AS mem (NOLOCK) ON mem.principal_id = srm.member_principal_id
	INNER JOIN sys.server_principals AS rol (NOLOCK) ON rol.principal_id = srm.role_principal_id
WHERE rol.name = 'sysadmin'
ORDER BY mem.name").Tables[0];
        }
        #endregion

        #region Disk
        /// <summary>
        /// Get information (size, freespace, name, last autogrowth) for each databases files.
        /// </summary>
        /// <param name="s">your smo server</param>
        /// <param name="minSize">Use this param to hide files smaller than this size in Mb(0 by default)</param>
        /// <param name="maxSize">Use this param to hide files greater than this size in Mb(2147483647 by default)</param>
        /// <param name="withData">To get data files (true by default)</param>
        /// <param name="withLog">To get log files (true by default)</param>
        /// <returns>a DataTable</returns>
        public static DataTable GetFileTreeMaps(this smo.Server s, int minSize = 0, int maxSize = 2147483647, bool withData = true, bool withLog = true)
        {
            smo.Database d = s.Databases["master"];
            string fileTypeFilter = string.Empty;

            if (!withData)
            {
                fileTypeFilter += " AND b.TYPE != 0 ";
            }
            if (!withLog)
            {
                fileTypeFilter += " AND b.TYPE = 0 ";
            }

            string sql = string.Format(@"CREATE TABLE #TMPLASTFILECHANGE 
(
	databasename nvarchar(128)
	, filename nvarchar(128)
	, endtime datetime
)

DECLARE @path NVARCHAR(1000)
SELECT @path = SUBSTRING(PATH, 1, LEN(PATH) - CHARINDEX('\', REVERSE(PATH))) + '\log.trc'
FROM sys.traces
WHERE id = 1;

WITH CTE (databaseid, filename, EndTime)
AS
(
	SELECT databaseid
		, filename
		, MAX(t.EndTime) AS EndTime
	FROM ::fn_trace_gettable(@path, default ) t
	WHERE EventClass IN (92, 93)
		AND DATEDIFF(hh,StartTime,GETDATE()) < 24
	GROUP BY databaseid
		, filename
)
INSERT INTO #TMPLASTFILECHANGE
(
	databasename
	, filename
	, endtime
)
SELECT DB_NAME(database_id) AS DatabaseName
	, mf.name AS LogicalName
	, cte.EndTime
FROM sys.master_files mf
	LEFT JOIN CTE cte on mf.database_id=cte.databaseid 
		AND mf.name=cte.filename
WHERE cte.EndTime IS NOT NULL

CREATE TABLE #TMPSPACEUSED 
(
	DBNAME    NVARCHAR(128),
	FILENAME   NVARCHAR(128),
	SPACEUSED FLOAT
)

INSERT INTO #TMPSPACEUSED
(
	DBNAME
	, FILENAME
	, SPACEUSED
)
EXEC('sp_MSforeachdb''use [?]; Select ''''?'''' AS DBName
			, Name AS FileNme
			, fileproperty(Name,''''SpaceUsed'''') AS SpaceUsed 
		FROM sys.sysfiles''')

SELECT a.name AS DatabaseName
	, b.name AS FileName
	, CASE b.TYPE WHEN 0 THEN 'DATA' ELSE b.type_desc END AS FileType
	, CAST((b.size * 8 / 1024.0) AS DECIMAL(18,2)) AS FileSize
	, CAST((b.size * 8 / 1024.0) - (d.SPACEUSED / 128.0) AS DECIMAL(15,2)) / CAST((b.size * 8 / 1024.0) AS DECIMAL(18,2)) * 100 AS FreeSpace
	, b.physical_name
	, datediff(DAY, c.endtime, GETDATE()) AS LastGrowth
FROM sys.databases a
	INNER JOIN sys.master_files b ON a.database_id = b.database_id
	INNER JOIN #TMPSPACEUSED d  ON a.NAME = d.DBNAME 
		AND b.name = d.FILENAME
	LEFT JOIN #TMPLASTFILECHANGE c on a.name = c.databasename 
		AND b.name = c.filename
WHERE b.size >= ({0} / 8.0 * 1024.0)
	AND b.size <= ({1} / 8.0 * 1024.0)
    {2}
ORDER BY FILESIZE DESC

DROP TABLE #TMPSPACEUSED
DROP TABLE #TMPLASTFILECHANGE
", minSize, maxSize, fileTypeFilter);
            return d.ExecuteWithResults(sql).Tables[0];
        }

        /// <summary>
        /// Get IO statistics by database files.
        /// Useful to detect disk performance issue or high database IO
        /// </summary>
        /// <param name="s">your smo server</param>
        /// <returns>return the result of the query in a DataTable</returns>
        public static DataTable GetIOStatistics(this smo.Server s)
        {
            smo.Database d = s.Databases["master"];
            return d.ExecuteWithResults(@"SELECT DB_NAME(fs.database_id) AS [Database Name]
	, mf.physical_name AS [Physical Name]
	, io_stall_read_ms AS [IO Stall Read ms]
	, num_of_reads AS [Num of Reads]
	, CAST(io_stall_read_ms/(1.0 + num_of_reads) AS NUMERIC(10,1)) AS [Avg Read Stall ms]
	, io_stall_write_ms AS [IO Stall Write ms]
	, num_of_writes AS [Num of Writes]
	, CAST(io_stall_write_ms/(1.0+num_of_writes) AS NUMERIC(10,1)) AS [Avg Write Stall ms]
	, io_stall_read_ms + io_stall_write_ms AS [IO Stalls]
	, num_of_reads + num_of_writes AS [Total IO]
	, CAST((io_stall_read_ms + io_stall_write_ms)/(1.0 + num_of_reads + num_of_writes) AS NUMERIC(10,1)) AS [Avg IO Stall ms]
FROM sys.dm_io_virtual_file_stats(null,null) AS fs 
	INNER JOIN sys.master_files AS mf (NOLOCK) ON fs.database_id = mf.database_id 
		AND fs.[file_id] = mf.[file_id]
ORDER BY [Avg IO Stall ms] DESC 
OPTION (RECOMPILE)").Tables[0];
        }

        /// <summary>
        /// Get free disk space by logical drive. If Ole automation procedures are enable, free disk space is available in percentage.
        /// </summary>
        /// <param name="s">your smo server</param>
        /// <returns>datable with logical name + free disk space</returns>
        public static DataTable GetFreeDiskSpace(this smo.Server s)
        {
            string sql = @"DECLARE @t TABLE(
	drive VARCHAR(2)
	, TotalSize BIGINT DEFAULT 0
	, FreeSpace BIGINT)
INSERT INTO @t(drive, FreeSpace)
EXEC xp_fixeddrives
SELECT drive
	, TotalSize
	, FreeSpace
FROM @t";
            if (s.Configuration.OleAutomationProceduresEnabled.RunValue == 1)
            {
                sql = @"SET NOCOUNT ON
DECLARE @hr INT
DECLARE @fso INT
DECLARE @drive CHAR(1)
DECLARE @odrive INT
DECLARE @TotalSize VARCHAR(20)
DECLARE @MB NUMERIC(18, 2)
SET @MB = 1048576

CREATE TABLE #drives(
	drive CHAR(1) PRIMARY KEY
	, FreeSpace INT NULL
	, TotalSize INT NULL)

INSERT #drives(drive,FreeSpace)
EXEC master.dbo.xp_fixeddrives

EXEC @hr = sp_OACreate 'Scripting.FileSystemObject', @fso OUT
IF @hr <> 0 EXEC sp_OAGetErrorInfo @fso

DECLARE dcur CURSOR LOCAL FAST_FORWARD
FOR SELECT drive
	FROM #drives
	ORDER by drive
OPEN dcur FETCH NEXT FROM dcur INTO @drive
WHILE @@FETCH_STATUS = 0
BEGIN
	EXEC @hr = sp_OAMethod @fso, 'GetDrive', @odrive OUT, @drive
	IF @hr <> 0 EXEC sp_OAGetErrorInfo @fso
	EXEC @hr = sp_OAGetProperty @odrive, 'TotalSize', @TotalSize OUT
	IF @hr <> 0 EXEC sp_OAGetErrorInfo @odrive

	UPDATE #drives
		SET TotalSize = @TotalSize / @MB
	WHERE drive = @drive
FETCH NEXT FROM dcur INTO @drive
END
CLOSE dcur
DEALLOCATE dcur
EXEC @hr = sp_OADestroy @fso
IF @hr <> 0 EXEC sp_OAGetErrorInfo @fso
SELECT drive
	, TotalSize
	, FreeSpace
FROM #drives
ORDER BY drive
DROP TABLE #drives";
            }
            smo.Database d = s.Databases["master"];
            return d.ExecuteWithResults(sql).Tables[0];
        }
        #endregion

        #region CPU
        /// <summary>
        /// Get the CPU usage by database from query execution statistics
        /// </summary>
        /// <param name="s">your smo server</param>
        /// <returns>return the result of the query in a DataTable</returns>
        public static DataTable GetCPUbyDatabase(this smo.Server s)
        {
            smo.Database d = s.Databases["master"];
            string sql = @"WITH DB_CPU_Stats AS
(
	SELECT DatabaseID
		, DB_Name(DatabaseID) AS DatabaseName
		, SUM(total_worker_time) / 1000 AS CPU_Time_Ms
    FROM sys.dm_exec_query_stats AS qs (NOLOCK)
    CROSS APPLY (SELECT CONVERT(int, value) AS DatabaseID
                FROM sys.dm_exec_plan_attributes(qs.plan_handle)
                WHERE attribute = N'dbid') AS F_DB
    GROUP BY DatabaseID
)
SELECT DatabaseName AS [Database Name]
	, CPU_Time_Ms AS [CPU Time Ms]
	, CAST(CPU_Time_Ms * 1.0 / SUM(CPU_Time_Ms) OVER() * 100.0 AS DECIMAL(5, 2)) AS [CPU Percent]	
FROM DB_CPU_Stats 
WHERE DatabaseID != 32767 
ORDER BY ROW_NUMBER() OVER(ORDER BY CPU_Time_Ms DESC) OPTION (RECOMPILE)";
            return d.ExecuteWithResults(sql).Tables[0];
        }
        
        /// <summary>
        /// Get the CPU usage history (256 last ticks) from dm_os_ring_buffers
        /// This dmv is not supported by Microsoft...
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        public static DataTable GetCPUFromRing(this smo.Server s)
        {
            smo.Database d = s.Databases["master"];
            string sql = @"DECLARE @ts_now bigint
SELECT @ts_now = cpu_ticks / (cpu_ticks / ms_ticks)
FROM sys.dm_os_sys_info
;WITH ring AS
(
    SELECT
       record.value('(Record/@id)[1]', 'int') AS record_id,
       DATEADD (ms, -1 * (@ts_now - [timestamp]), GETDATE()) AS EventTime,
       100-record.value('(Record/SchedulerMonitorEvent/SystemHealth/SystemIdle)[1]', 'int') AS system_cpu_utilization_post_sp2,
       record.value('(Record/SchedulerMonitorEvent/SystemHealth/ProcessUtilization)[1]', 'int') AS sql_cpu_utilization_post_sp2 ,
       100-record.value('(Record/SchedluerMonitorEvent/SystemHealth/SystemIdle)[1]', 'int') AS system_cpu_utilization_pre_sp2,
       record.value('(Record/SchedluerMonitorEvent/SystemHealth/ProcessUtilization)[1]', 'int') AS sql_cpu_utilization_pre_sp2
     FROM (
       SELECT timestamp, CONVERT (xml, record) AS record
       FROM sys.dm_os_ring_buffers
       WHERE ring_buffer_type = 'RING_BUFFER_SCHEDULER_MONITOR'
         AND record LIKE '%<SystemHealth>%') AS t
), cte AS
(
    SELECT EventTime
        , CASE WHEN system_cpu_utilization_post_sp2 IS NOT NULL THEN system_cpu_utilization_post_sp2 ELSE system_cpu_utilization_pre_sp2 END AS system_cpu
        , CASE WHEN sql_cpu_utilization_post_sp2 IS NOT NULL THEN sql_cpu_utilization_post_sp2 ELSE sql_cpu_utilization_pre_sp2 END AS sql_cpu
    FROM ring
)
SELECT EventTime
    , system_cpu
    , CASE WHEN sql_cpu > system_cpu THEN sql_cpu / 2 ELSE sql_cpu END AS sql_cpu
FROM cte
ORDER BY EventTime DESC";
            return d.ExecuteWithResults(sql).Tables[0];
        }
        #endregion

        #region Memory
        /// <summary>
        /// Get Buffer Size by Database
        /// </summary>
        /// <param name="s">your smo server</param>
        /// <returns>return the result of the query in a DataTable</returns>
        public static DataTable GetBufferByDatabase(this smo.Server s)
        {
            smo.Database d = s.Databases["master"];
            string sql = @"SELECT CASE WHEN database_id = 32767 THEN 'Resource' ELSE DB_NAME(database_id) END AS [Database]
	, COUNT(*) / 128 AS [Cached_Size]
FROM sys.dm_os_buffer_descriptors (NOLOCK)
GROUP BY database_id
ORDER BY [Cached_Size] DESC
OPTION (RECOMPILE)";
            return d.ExecuteWithResults(sql).Tables[0];
        }
        #endregion

        #region Waits
        /// <summary>
        /// Get Wait statistics for an instance. Very helpful to understand performance issues
        /// </summary>
        /// <param name="s">your smo server</param>
        /// <param name="listWaitToIgnore">List of wait stats to ignore with simple quote and separated by coma. Example : 'WaitStatToIgnore1', 'WaitStatToIgnore2' </param>
        /// <returns>return the result of the query in a DataTable</returns>
        public static DataTable GetWaitStatistics(this smo.Server s, string listWaitToIgnore = "'BROKER_EVENTHANDLER', 'BROKER_RECEIVE_WAITFOR', 'BROKER_TASK_STOP', 'BROKER_TO_FLUSH', 'BROKER_TRANSMITTER', 'CHECKPOINT_QUEUE', 'CHKPT', 'CLR_AUTO_EVENT', 'CLR_MANUAL_EVENT', 'CLR_SEMAPHORE', 'DBMIRROR_DBM_EVENT', 'DBMIRROR_EVENTS_QUEUE', 'DBMIRROR_WORKER_QUEUE', 'DBMIRRORING_CMD', 'DIRTY_PAGE_POLL', 'DISPATCHER_QUEUE_SEMAPHORE', 'EXECSYNC', 'FSAGENT', 'FT_IFTS_SCHEDULER_IDLE_WAIT', 'FT_IFTSHC_MUTEX', 'HADR_CLUSAPI_CALL', 'HADR_FILESTREAM_IOMGR_IOCOMPLETION', 'HADR_LOGCAPTURE_WAIT', 'HADR_NOTIFICATION_DEQUEUE', 'HADR_TIMER_TASK', 'HADR_WORK_QUEUE', 'KSOURCE_WAKEUP', 'LAZYWRITER_SLEEP', 'LOGMGR_QUEUE', 'ONDEMAND_TASK_QUEUE', 'PWAIT_ALL_COMPONENTS_INITIALIZED', 'QDS_PERSIST_TASK_MAIN_LOOP_SLEEP', 'QDS_CLEANUP_STALE_QUERIES_TASK_MAIN_LOOP_SLEEP', 'REQUEST_FOR_DEADLOCK_SEARCH', 'RESOURCE_QUEUE', 'SERVER_IDLE_CHECK', 'SLEEP_BPOOL_FLUSH', 'SLEEP_DBSTARTUP', 'SLEEP_DCOMSTARTUP', 'SLEEP_MASTERDBREADY', 'SLEEP_MASTERMDREADY', 'SLEEP_MASTERUPGRADED', 'SLEEP_MSDBSTARTUP', 'SLEEP_SYSTEMTASK', 'SLEEP_TASK', 'SLEEP_TEMPDBSTARTUP', 'SNI_HTTP_ACCEPT', 'SP_SERVER_DIAGNOSTICS_SLEEP', 'SQLTRACE_BUFFER_FLUSH', 'SQLTRACE_INCREMENTAL_FLUSH_SLEEP', 'SQLTRACE_WAIT_ENTRIES', 'WAIT_FOR_RESULTS', 'WAITFOR', 'WAITFOR_TASKSHUTDOWN', 'WAIT_XTP_HOST_WAIT', 'WAIT_XTP_OFFLINE_CKPT_NEW_LOG', 'WAIT_XTP_CKPT_CLOSE', 'XE_DISPATCHER_JOIN', 'XE_DISPATCHER_WAIT', 'XE_TIMER_EVENT'")
        {
            smo.Database d = s.Databases["master"];
            string sqlIgnore = string.Empty;
            if (!string.IsNullOrEmpty(listWaitToIgnore))
            {
                sqlIgnore = "AND [wait_type] NOT IN (" + listWaitToIgnore + ")";
            }
            string sql = string.Format(@"WITH [Waits] AS
(
	SELECT
		[wait_type],
		[wait_time_ms] / 1000.0 AS [WaitS],
		([wait_time_ms] - [signal_wait_time_ms]) / 1000.0 AS [ResourceS],
		[signal_wait_time_ms] / 1000.0 AS [SignalS],
		[waiting_tasks_count] AS [WaitCount],
		100.0 * [wait_time_ms] / SUM ([wait_time_ms]) OVER() AS [Percentage],
		ROW_NUMBER() OVER(ORDER BY [wait_time_ms] DESC) AS [RowNum]
	FROM sys.dm_os_wait_stats
	WHERE [waiting_tasks_count] > 0
		{0}
)
SELECT
	MAX ([W1].[wait_type]) AS [wait_type],
	CAST (MAX ([W1].[WaitS]) AS DECIMAL (16,2)) AS [wait_time_s],
	CAST (MAX ([W1].[ResourceS]) AS DECIMAL (16,2)) AS [Resource_S],
	CAST (MAX ([W1].[SignalS]) AS DECIMAL (16,2)) AS [Signal_S],
	MAX ([W1].[WaitCount]) AS [waiting_tasks_count],
	CAST (MAX ([W1].[Percentage]) AS DECIMAL (5,2)) AS [pct],
	CAST ((MAX ([W1].[WaitS]) / MAX ([W1].[WaitCount])) AS DECIMAL (16,4)) AS [AvgWait_S],
	CAST ((MAX ([W1].[ResourceS]) / MAX ([W1].[WaitCount])) AS DECIMAL (16,4)) AS [AvgRes_S],
	CAST ((MAX ([W1].[SignalS]) / MAX ([W1].[WaitCount])) AS DECIMAL (16,4)) AS [AvgSig_S]
FROM [Waits] AS [W1]
	INNER JOIN [Waits] AS [W2] ON [W2].[RowNum] <= [W1].[RowNum]
GROUP BY [W1].[RowNum]
HAVING SUM ([W2].[Percentage]) - MAX ([W1].[Percentage]) < 99;", sqlIgnore);
            return d.ExecuteWithResults(sql).Tables[0];
        }
        #endregion

        #region ErrorLogs
        /// <summary>
        /// Read ErrorLog files with some improvements :
        /// Doesn't return successful or failed login
        /// Remove useless message
        /// Filter by date
        /// Filter Informationnal messages
        /// </summary>
        /// <param name="s">Your smo server</param>
        /// <param name="startTime">Doesn't return events before this datetime. You could use DateTime.MinValue if you don't want to filter.</param>
        /// <param name="endTime">Doesn't return events after this datetime. You could use DateTime.MaxValue if you don't want to filter.</param>
        /// <param name="logNumber">The file you want to parse. File 0 by default</param>
        /// <param name="withInformationMessage">Include informationnal messages.</param>
        /// <returns>a DataTable with the result of the query</returns>
        public static DataTable ReadErrorLog(this smo.Server s, DateTime startTime, DateTime endTime, int logFileNumber = 0, bool withInformationMessage = false)
        {
            string sql = string.Format(@"CREATE TABLE #KMOErrorLog
(
    LogDate DATETIME
    , ProcessInfo NVARCHAR(50)
    ,vchMessage NVARCHAR(2000)
)

INSERT INTO #KMOErrorLog(LogDate, ProcessInfo, vchMessage)
EXEC master.dbo.xp_readerrorlog {0}

DELETE FROM #KMOErrorLog 
WHERE LogDate < '{1}'
    OR LogDate > '{2}'
    OR ProcessInfo = 'Logon'
    OR vchMessage LIKE 'Error: %, Severity: %, State: %.'", logFileNumber, startTime.ToString("yyyyMMdd HH:mm:ss"), endTime.ToString("yyyyMMdd HH:mm:ss"));

            if (!withInformationMessage)
            {
                sql += @" 
DELETE FROM #KMOErrorLog
WHERE vchMessage LIKE '%This is an informational message%'
    OR vchMessage LIKE '%DBCC CHECKDB%found 0 errors and repaired 0 errors%'
    OR vchMessage LIKE '%No user action is required%'
    OR vchMessage LIKE '%No user action required%'
    OR vchMessage LIKE '%Ce message est fourni à titre d''information. Aucune action n''est requise de la part de l''utilisateur.%' ";
            }

            sql += @" SELECT LogDate
    , ProcessInfo
    , RTRIM(LTRIM(vchMessage)) AS [Message]
FROM #KMOErrorLog
ORDER BY LogDate DESC

DROP TABLE #KMOErrorLog
";

            smo.Database d = s.Databases["master"];
            return d.ExecuteWithResults(sql).Tables[0];
        }

        /// <summary>
        /// Get Login Failed from SQL ErrorLog
        /// </summary>
        /// <param name="s">Your smo Server</param>
        /// <param name="startTime">Doesn't return events before this datetime. You could use DateTime.MinValue if you don't want to filter.</param>
        /// <param name="endTime">Doesn't return events after this datetime. You could use DateTime.MaxValue if you don't want to filter.</param>
        /// <param name="logNumber">The file you want to parse. File 0 by default</param>
        /// <returns>a DataTable</returns>
        public static DataTable GetLoginFailed(this smo.Server s, DateTime startTime, DateTime endTime, int logFileNumber = 0)
        {
            string sql = string.Format(@"CREATE TABLE #KMOLoginFailed
(
    LogDate DATETIME
    , ProcessInfo NVARCHAR(50)
    , vchMessage NVARCHAR(2000)
)

INSERT INTO #KMOLoginFailed(LogDate, ProcessInfo, vchMessage)
EXEC master.dbo.xp_readerrorlog {0}

SELECT LogDate
    , ProcessInfo
	, RTRIM(LTRIM(vchMessage)) AS [Message]
FROM #KMOLoginFailed
WHERE (SUBSTRING(vchMessage,1, 12) = 'Login failed'
	OR vchMessage LIKE '%SSPI%'
    OR processinfo = 'Logon')
	AND logdate > '{1}'
	AND logdate < '{2}'
ORDER BY LogDate DESC

DROP TABLE #KMOLoginFailed", logFileNumber, startTime.ToString("yyyyMMdd HH:mm:ss"), endTime.ToString("yyyyMMdd HH:mm:ss"));

            smo.Database d = s.Databases["master"];
            return d.ExecuteWithResults(sql).Tables[0];
        }

        #endregion

        #region Queries
        /// <summary>
        /// Get the top N stored procedures more expensive
        /// </summary>
        /// <param name="s">Your smo server</param>
        /// <param name="orderQuery">With this parameter, you're able to sort the query. By default, sorted by Execution Count</param>
        /// <param name="rowCount">Number of rows returned by the query. 50 by default</param>
        /// <returns></returns>
        public static DataTable GetTop50StoredProcedures(this smo.Server s, string orderQuery = "Execution Count", int rowCount = 50)
        {
            if (s.VersionMajor < 10)
            {
                throw new Exception("This feature is only available from SQL Server 2008.");
            }

            string sql = string.Format(@"SELECT TOP ({0}) CASE WHEN database_id = 32767 THEN 'Resource' ELSE DB_NAME(database_id) END AS [Database]
	, OBJECT_SCHEMA_NAME(object_id, database_id) + '.' + OBJECT_NAME(object_id,database_id) AS [Stored Procedure]
	, cached_time AS [Cached Time]
	, last_execution_time AS [Last Execution Time]
	, execution_count AS [Execution Count]
	, total_worker_time / execution_count AS [Average CPU]
	, total_elapsed_time / execution_count AS [Average Elapsed Time]
	, total_logical_reads / execution_count AS [Average Logical Reads]
	, total_logical_writes / execution_count AS [Average Logical Writes]
	, total_physical_reads  / execution_count AS [Average Physical Reads]
FROM sys.dm_exec_procedure_stats (NOLOCK)
ORDER BY {1} DESC", rowCount, orderQuery);

            smo.Database d = s.Databases["master"];
            return d.ExecuteWithResults(sql).Tables[0];
        }

        /// <summary>
        /// Get the top N queries more expensive
        /// </summary>
        /// <param name="s">Your smo server</param>
        /// <param name="orderQuery">With this parameter, you're able to sort the query. By default, sorted by Execution Count</param>
        /// <param name="rowCount">Number of rows returned by the query. 50 by default</param>
        /// <returns></returns>
        public static DataTable GetTop50Queries(this smo.Server s, string orderQuery = "Execution Count", int rowCount = 50)
        {
            string sql = string.Format(@"SELECT TOP ({0}) CASE WHEN qt.dbid = 32767 THEN 'Resource' ELSE DB_NAME(qt.dbid) END AS [Database]
	, SUBSTRING(qt.text, qs.statement_start_offset / 2 + 1, (CASE WHEN qs.statement_end_offset = -1 THEN LEN(CONVERT(NVARCHAR(MAX), qt.text)) * 2  ELSE qs.statement_end_offset END - qs.statement_start_offset)/2) AS Query
	, execution_count AS [Execution Count]
	, total_worker_time/execution_count/1000 AS [Average Worker Time]
	, total_physical_reads/execution_count AS [Average Physical Reads]
	, total_logical_reads/execution_count AS [Average Logical Reads]
	, total_logical_writes/execution_count AS [Average Logical Writes]
	, total_elapsed_time/execution_count/1000 AS [Average Elapsed Time]
	, qt.text AS [Parent Query]
FROM sys.dm_exec_query_stats AS qs
	CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) AS qt
ORDER BY {1} DESC", rowCount, orderQuery);

            smo.Database d = s.Databases["master"];
            return d.ExecuteWithResults(sql).Tables[0];
        }

        #endregion
    }
}
