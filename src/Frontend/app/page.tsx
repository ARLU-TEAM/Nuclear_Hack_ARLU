"use client"

import { useCallback, useEffect, useMemo, useState } from "react"
import { AlertCircle, CheckCircle2, ClipboardList, FileSpreadsheet, FolderOpen, Loader2, Upload, Users, X } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Checkbox } from "@/components/ui/checkbox"
import { Label } from "@/components/ui/label"
import { cn } from "@/lib/utils"

type Status = "idle" | "loading" | "success" | "error"

interface OperationState {
  status: Status
  message: string
}

interface ProvisioningResponse {
  success?: boolean
  message?: string
  dryRun?: boolean
  groupsCount?: number
  studentsCount?: number
  actionsCount?: number
  execution?: {
    success?: boolean
    message?: string
    warnings?: string[]
  }
  groups?: Array<{
    execution?: {
      success?: boolean
      message?: string
      warnings?: string[]
    }
  }>
  summary?: {
    parsedFiles?: number
    filesWithErrors?: number
    executedGroups?: number
    executionFailedGroups?: number
  }
}

const API_BASE = process.env.NEXT_PUBLIC_API_URL?.trim() ?? "http://localhost:5101"

function resolveApiUrl(path: string): string {
  if (!API_BASE) {
    return path
  }

  const base = API_BASE.replace(/\/+$/, "").replace(/\/api$/i, "")
  return `${base}${path}`
}

function fileToGroupName(fileName: string): string {
  const index = fileName.lastIndexOf(".")
  return (index > 0 ? fileName.slice(0, index) : fileName).trim()
}

async function requestJson<T>(path: string, init: RequestInit): Promise<T> {
  const response = await fetch(resolveApiUrl(path), init)
  const text = await response.text()

  let payload: unknown
  try {
    payload = text ? JSON.parse(text) : {}
  } catch {
    payload = { message: text || "Request failed" }
  }

  if (!response.ok) {
    throw payload
  }

  return payload as T
}

function getErrorMessage(error: unknown, fallback: string): string {
  if (typeof error === "string" && error.trim()) {
    return error
  }

  if (typeof error === "object" && error !== null) {
    const e = error as Record<string, unknown>
    if (typeof e.message === "string" && e.message.trim()) {
      return e.message
    }
    if (typeof e.error === "string" && e.error.trim()) {
      return e.error
    }
  }

  return fallback
}

function formatSuccessMessage(prefix: string, response: ProvisioningResponse): string {
  const groups = response.groupsCount ?? 0
  const students = response.studentsCount ?? 0
  const actions = response.actionsCount ?? 0
  const message = response.message?.trim()

  if (message) {
    return `${prefix}: ${message}`
  }

  return `${prefix}: групп ${groups}, студентов ${students}, действий ${actions}`
}

function collectExecutionWarnings(response: ProvisioningResponse): string[] {
  const warnings: string[] = []
  const push = (value?: string[]) => {
    if (!value) return
    for (const item of value) {
      const text = item?.trim()
      if (text && !warnings.includes(text)) {
        warnings.push(text)
      }
    }
  }

  push(response.execution?.warnings)
  for (const group of response.groups ?? []) {
    push(group.execution?.warnings)
  }

  return warnings
}

function hasTaskSourceProblem(warnings: string[]): boolean {
  return warnings.some((w) =>
    w.includes("Папка 'Задания' не найдена") || w.includes("не найдено исходных файлов по папкам 'Работа N'"),
  )
}

async function executeProvisioning(files: File[], assignTasks: boolean): Promise<ProvisioningResponse> {
  const formData = new FormData()
  for (const file of files) {
    formData.append("files", file)
  }

  return requestJson<ProvisioningResponse>(`/api/provisioning/execute?dryRun=false&assignTasks=${assignTasks}`, {
    method: "POST",
    body: formData,
  })
}

const StatusIndicator = ({ state }: { state: OperationState }) => {
  if (state.status === "idle") {
    return null
  }

  return (
    <div
      className={cn(
        "mt-4 flex items-center gap-2 rounded-md p-3 text-sm select-none",
        "animate-in fade-in slide-in-from-top-2 duration-300",
        state.status === "loading" && "bg-secondary",
        state.status === "success" && "bg-primary/10",
        state.status === "error" && "bg-destructive/10",
      )}
      style={{
        color:
          state.status === "loading"
            ? "#4a5568"
            : state.status === "success"
              ? "#0066cc"
              : "#dc2626",
      }}
    >
      {state.status === "loading" && <Loader2 className="h-4 w-4 animate-spin shrink-0" />}
      {state.status === "success" && <CheckCircle2 className="h-4 w-4 shrink-0" />}
      {state.status === "error" && <AlertCircle className="h-4 w-4 shrink-0" />}
      <span className="font-medium">{state.message}</span>
    </div>
  )
}

export default function TFlexDocsPanel() {
  const [files, setFiles] = useState<File[]>([])
  const [useServiceFolder, setUseServiceFolder] = useState(false)
  const [isDragging, setIsDragging] = useState(false)
  const [usersState, setUsersState] = useState<OperationState>({ status: "idle", message: "" })

  const [groups, setGroups] = useState<string[]>([])
  const [selectedGroups, setSelectedGroups] = useState<string[]>([])
  const [tasksState, setTasksState] = useState<OperationState>({ status: "idle", message: "" })

  const acceptedExtensions = [".xlsx", ".csv", ".xml"]

  const uniqueGroupNames = useMemo(() => {
    const names = files.map((file) => fileToGroupName(file.name)).filter(Boolean)
    return Array.from(new Set(names))
  }, [files])

  useEffect(() => {
    setGroups(uniqueGroupNames)
    setSelectedGroups((previous) => previous.filter((group) => uniqueGroupNames.includes(group)))
  }, [uniqueGroupNames])

  const validateFile = (file: File): boolean => {
    const extension = "." + (file.name.split(".").pop()?.toLowerCase() ?? "")
    return acceptedExtensions.includes(extension)
  }

  const handleDragOver = useCallback(
    (event: React.DragEvent) => {
      event.preventDefault()
      if (!useServiceFolder) {
        setIsDragging(true)
      }
    },
    [useServiceFolder],
  )

  const handleDragLeave = useCallback((event: React.DragEvent) => {
    event.preventDefault()
    setIsDragging(false)
  }, [])

  const handleDrop = useCallback(
    (event: React.DragEvent) => {
      event.preventDefault()
      setIsDragging(false)
      if (useServiceFolder) {
        return
      }

      const droppedFiles = Array.from(event.dataTransfer.files).filter(validateFile)
      if (droppedFiles.length > 0) {
        setFiles((previous) => [...previous, ...droppedFiles])
        setUsersState({ status: "idle", message: "" })
      } else {
        setUsersState({ status: "error", message: "Неподдерживаемый формат файла. Используйте CSV/XML/XLSX." })
      }
    },
    [useServiceFolder],
  )

  const handleFileSelect = (event: React.ChangeEvent<HTMLInputElement>) => {
    const selected = Array.from(event.target.files ?? []).filter(validateFile)
    if (selected.length > 0) {
      setFiles((previous) => [...previous, ...selected])
      setUsersState({ status: "idle", message: "" })
      setTasksState({ status: "idle", message: "" })
    }
    event.target.value = ""
  }

  const removeFile = (index: number) => {
    setFiles((previous) => previous.filter((_, i) => i !== index))
  }

  const handleServiceFolderChange = (checked: boolean) => {
    setUseServiceFolder(checked)

    if (checked) {
      setUsersState({
        status: "error",
        message: "Режим служебной папки в этой версии API не поддерживается. Загрузите файлы вручную.",
      })
    } else {
      setUsersState({ status: "idle", message: "" })
    }
  }

  const handleCreateUsers = async () => {
    if (useServiceFolder) {
      setUsersState({
        status: "error",
        message: "Служебная папка не подключена к API. Используйте загрузку файлов.",
      })
      return
    }

    if (files.length === 0) {
      setUsersState({ status: "error", message: "Выберите файлы с группами (CSV/XML/XLSX)." })
      return
    }

    setUsersState({ status: "loading", message: "Выполняется provisioning..." })

    try {
      const result = await executeProvisioning(files, false)
      const success = result.success === true
      const warnings = collectExecutionWarnings(result)
      const warningSuffix = warnings.length > 0 ? ` Предупреждение: ${warnings[0]}` : ""
      setUsersState({
        status: success ? "success" : "error",
        message: success
          ? `${formatSuccessMessage("Пользователи и папки обработаны", result)}.${warningSuffix}`.trim()
          : (result.message ?? "Provisioning завершился с ошибкой"),
      })
    } catch (error) {
      setUsersState({
        status: "error",
        message: getErrorMessage(error, "Ошибка запроса при создании пользователей."),
      })
    }
  }

  const toggleGroup = (group: string) => {
    setSelectedGroups((previous) =>
      previous.includes(group) ? previous.filter((value) => value !== group) : [...previous, group],
    )
  }

  const handleDistributeTasks = async () => {
    if (useServiceFolder) {
      setTasksState({
        status: "error",
        message: "Служебная папка не подключена к API. Используйте загрузку файлов.",
      })
      return
    }

    if (selectedGroups.length === 0) {
      setTasksState({ status: "error", message: "Выберите хотя бы одну группу." })
      return
    }

    const selectedFiles = files.filter((file) => selectedGroups.includes(fileToGroupName(file.name)))
    if (selectedFiles.length === 0) {
      setTasksState({ status: "error", message: "Для выбранных групп не найдены загруженные файлы." })
      return
    }

    setTasksState({ status: "loading", message: "Выполняется распределение заданий..." })

    try {
      const result = await executeProvisioning(selectedFiles, true)
      const success = result.success === true
      const warnings = collectExecutionWarnings(result)
      const tasksMissing = hasTaskSourceProblem(warnings)
      setTasksState({
        status: success && !tasksMissing ? "success" : "error",
        message:
          success && !tasksMissing
            ? formatSuccessMessage("Задания распределены", result)
            : (warnings[0] ?? result.message ?? "Распределение завершилось с ошибкой"),
      })
    } catch (error) {
      setTasksState({
        status: "error",
        message: getErrorMessage(error, "Ошибка запроса при распределении заданий."),
      })
    }
  }

  const canCreateUsers = !useServiceFolder && files.length > 0

  return (
    <div className="min-h-screen bg-background p-4 md:p-8 flex items-center justify-center">
      <style>{`
        .action-btn {
          position: relative;
          overflow: hidden;
          transition: transform 0.15s ease, box-shadow 0.15s ease;
        }
        .action-btn:hover:not(:disabled) {
          transform: translateY(-1px);
          box-shadow: 0 4px 16px oklch(from var(--primary) l c h / 0.35);
        }
        .action-btn:active:not(:disabled) {
          transform: translateY(0);
          box-shadow: none;
        }
        .panel-card {
          transition: box-shadow 0.25s ease;
        }
        .panel-card:hover {
          box-shadow: 0 4px 24px oklch(from var(--primary) l c h / 0.08);
        }
        .dropzone {
          transition: all 0.2s ease;
        }
        .dropzone:hover:not(.disabled) {
          border-color: var(--primary);
          background: oklch(from var(--primary) l c h / 0.03);
        }
        .dropzone.dragging {
          border-color: var(--primary);
          background: oklch(from var(--primary) l c h / 0.06);
          transform: scale(1.01);
        }
        .dropzone.disabled {
          opacity: 0.5;
          cursor: not-allowed;
        }
        .file-chip {
          transition: all 0.2s ease;
        }
        .file-chip:hover {
          background: oklch(from var(--primary) l c h / 0.12);
        }
        .remove-btn {
          transition: all 0.15s ease;
        }
        .remove-btn:hover {
          background: oklch(from var(--destructive) l c h / 0.15);
          color: var(--destructive);
        }
        .checkbox-row {
          transition: background 0.2s ease;
        }
        .checkbox-row:hover {
          background: oklch(from var(--primary) l c h / 0.04);
        }
        .group-btn {
          transition: all 0.15s ease;
        }
        .group-btn:hover:not(.selected) {
          border-color: var(--primary);
          background: oklch(from var(--primary) l c h / 0.04);
        }
        .group-btn.selected {
          border-color: var(--primary);
          background: oklch(from var(--primary) l c h / 0.1);
        }
      `}</style>

      <div className="w-full max-w-lg space-y-6">
        <header className="text-center">
          <h1 className="text-2xl font-bold tracking-tight select-none" style={{ color: "#000" }}>
            EngGraphLabAdminApp
          </h1>
          <p className="text-sm font-medium mt-1 select-none" style={{ color: "#4a5568" }}>
            Панель управления T-FLEX
          </p>
        </header>

        <Card className="panel-card">
          <CardHeader className="pb-4">
            <CardTitle className="text-lg font-bold flex items-center gap-2 select-none" style={{ color: "#000" }}>
              <Users className="h-5 w-5" style={{ color: "#0066cc" }} />
              Создание пользователей
            </CardTitle>
            <CardDescription className="select-none" style={{ color: "#4a5568" }}>
              Таблица: Фамилия, Имя, Отчество, Логин
            </CardDescription>
          </CardHeader>

          <CardContent className="space-y-4">
            <label className="checkbox-row flex items-start gap-3 p-3 rounded-lg border border-border cursor-pointer select-none">
              <Checkbox
                checked={useServiceFolder}
                onCheckedChange={(checked) => handleServiceFolderChange(checked === true)}
                className="mt-0.5"
              />
              <div className="flex-1">
                <span className="font-semibold block" style={{ color: "#000" }}>
                  Файл из служебной папки T-FLEX
                </span>
                <span className="text-xs block mt-0.5" style={{ color: "#64748b" }}>
                  В текущей API-версии режим недоступен. Используйте ручную загрузку файлов ниже.
                </span>
              </div>
            </label>

            {useServiceFolder && (
              <div className="border rounded-lg p-3 space-y-2 animate-in fade-in slide-in-from-top-2 duration-200">
                <div className="flex items-center gap-2 mb-2">
                  <FolderOpen className="h-4 w-4" style={{ color: "#0066cc" }} />
                  <span className="text-sm font-medium select-none" style={{ color: "#000" }}>
                    Служебная папка недоступна
                  </span>
                </div>
                <p className="text-sm py-1" style={{ color: "#64748b" }}>
                  Выключите режим и загрузите файлы групп вручную.
                </p>
              </div>
            )}

            {!useServiceFolder && (
              <div>
                <Label className="font-semibold mb-2 block select-none" style={{ color: "#000" }}>
                  Загрузите файлы
                </Label>

                <div
                  onDragOver={handleDragOver}
                  onDragLeave={handleDragLeave}
                  onDrop={handleDrop}
                  className={cn(
                    "dropzone relative border-2 border-dashed rounded-lg p-6 text-center min-h-[140px] flex flex-col items-center justify-center",
                    isDragging && "dragging",
                  )}
                >
                  <label className="cursor-pointer block">
                    <input
                      type="file"
                      accept=".xlsx,.csv,.xml"
                      onChange={handleFileSelect}
                      multiple
                      className="sr-only"
                    />
                    <Upload className="h-8 w-8 mx-auto mb-2" style={{ color: "#9ca3af" }} />
                    <p className="font-medium select-none" style={{ color: "#000" }}>
                      Перетащите файлы сюда
                    </p>
                    <p className="text-sm mt-1 select-none" style={{ color: "#64748b" }}>
                      или нажмите для выбора
                    </p>
                    <p className="text-xs mt-2 select-none" style={{ color: "#9ca3af" }}>
                      CSV, XML, XLSX (можно несколько)
                    </p>
                  </label>
                </div>

                {files.length > 0 && (
                  <div className="mt-3 space-y-2">
                    {files.map((file, index) => (
                      <div
                        key={`${file.name}-${index}`}
                        className="file-chip flex items-center gap-2 px-3 py-2 rounded-lg"
                        style={{ background: "oklch(from var(--primary) l c h / 0.06)" }}
                      >
                        <FileSpreadsheet className="h-4 w-4 shrink-0" style={{ color: "#0066cc" }} />
                        <span className="font-medium text-sm flex-1 truncate select-none" style={{ color: "#000" }}>
                          {file.name}
                        </span>
                        <span className="text-xs shrink-0 select-none" style={{ color: "#64748b" }}>
                          {(file.size / 1024).toFixed(1)} КБ
                        </span>
                        <button type="button" onClick={() => removeFile(index)} className="remove-btn p-1 rounded-full" aria-label="Удалить">
                          <X className="h-3.5 w-3.5" />
                        </button>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            )}

            <Button type="button" onClick={handleCreateUsers} disabled={usersState.status === "loading" || !canCreateUsers} className="action-btn w-full h-11 select-none">
              {usersState.status === "loading" ? <Loader2 className="h-4 w-4 animate-spin mr-2" /> : <Users className="h-4 w-4 mr-2" />}
              Создать пользователей
            </Button>

            <StatusIndicator state={usersState} />
          </CardContent>
        </Card>

        <Card className="panel-card">
          <CardHeader className="pb-4">
            <CardTitle className="text-lg font-bold flex items-center gap-2 select-none" style={{ color: "#000" }}>
              <ClipboardList className="h-5 w-5" style={{ color: "#0066cc" }} />
              Распределение заданий
            </CardTitle>
            <CardDescription className="select-none" style={{ color: "#4a5568" }}>
              Выберите группы из загруженных файлов и запустите выполнение для них
            </CardDescription>
          </CardHeader>

          <CardContent className="space-y-4">
            <div>
              <Label className="font-semibold mb-2 block select-none" style={{ color: "#000" }}>
                Выберите группу
              </Label>

              {groups.length === 0 ? (
                <p className="text-sm py-4 text-center select-none" style={{ color: "#64748b" }}>
                  Сначала загрузите файлы групп в блоке выше
                </p>
              ) : (
                <div className="border rounded-lg p-3">
                  <div className="flex items-center justify-between mb-2">
                    <span className="text-xs select-none" style={{ color: "#64748b" }}>
                      Выбрано: {selectedGroups.length} из {groups.length}
                    </span>
                    {selectedGroups.length > 0 && (
                      <button type="button" onClick={() => setSelectedGroups([])} className="text-xs hover:underline select-none" style={{ color: "#0066cc" }}>
                        Сбросить
                      </button>
                    )}
                  </div>
                  <div className="flex flex-wrap gap-2 max-h-48 overflow-y-auto">
                    {groups.map((group) => (
                      <button
                        type="button"
                        key={group}
                        onClick={() => toggleGroup(group)}
                        className={cn(
                          "group-btn px-3 py-1.5 rounded-lg border text-sm font-medium select-none",
                          selectedGroups.includes(group) && "selected",
                        )}
                        style={{ color: "#000" }}
                      >
                        {group}
                      </button>
                    ))}
                  </div>
                </div>
              )}
            </div>

            <Button
              type="button"
              onClick={handleDistributeTasks}
              disabled={tasksState.status === "loading" || selectedGroups.length === 0}
              className="action-btn w-full h-11 select-none"
            >
              {tasksState.status === "loading" ? <Loader2 className="h-4 w-4 animate-spin mr-2" /> : <ClipboardList className="h-4 w-4 mr-2" />}
              Распределить задания
            </Button>

            <StatusIndicator state={tasksState} />
          </CardContent>
        </Card>

        <footer className="text-center text-xs font-medium select-none" style={{ color: "#64748b" }}>
          Результат выполнения отображается по ответу backend API
        </footer>
      </div>
    </div>
  )
}
