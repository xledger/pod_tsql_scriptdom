# pod.xledger.tsql-scriptdom

[babashka](https://github.com/borkdude/babashka) [pod](https://github.com/babashka/babashka.pods) for using [Microsoft.SqlServer.TransactSql.ScriptDom]https://docs.microsoft.com/en-us/dotnet/api/microsoft.sqlserver.transactsql.scriptdom?view=sql-dacfx-150, a TSQL parsing / script generation library.

## Usage

```clojure
(require '[babashka.pods :as pods])

;; After compiling this solution with Visual Studio:
(pods/load-pod "C:/src/pod_tsql_scriptdom/bin/Debug/net5.0/pod.xledger.tsql_scriptdom.exe")
;; or, if you are not on Windows:
(pods/load-pod ["dotnet" "bin/Debug/net5.0/pod.xledger.tsql_scriptdom.dll"])

(require '[pod.xledger.tsql-scriptdom :as tsql-scriptdom])

(println (tsql-scriptdom/reformat-sql {
                                         :sql "select [name], (select [name], [max_length] from sys.parameters b where b.object_id = a.object_id for json path) as params from sys.procedures a for json path"
                                         :initial-quoted-identifiers false ;; the default   
                                         }))
SELECT [name],
       (SELECT [name],
               [max_length]
        FROM   sys.parameters AS b
        WHERE  b.object_id = a.object_id
        FOR    JSON PATH) AS params
FROM   sys.procedures AS a
FOR    JSON PATH;

=> nil