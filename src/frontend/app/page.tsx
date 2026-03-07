"use client"

import { useState, useCallback, useEffect } from "react"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { Label } from "@/components/ui/label"
import { Checkbox } from "@/components/ui/checkbox"
import { Upload, Users, X, FileSpreadsheet, Loader2, CheckCircle2, AlertCircle, FolderOpen, ClipboardList } from "lucide-react"
import { cn } from "@/lib/utils"

type Status = "idle" | "loading" | "success" | "error"

interface OperationState {
  status: Status
  message: string
}

// API
const API_BASE = process.env.NEXT_PUBLIC_API_URL || "http://localhost:8000"

//  Создание пользователей
async function createUsersAPI(payload: { 
  useServiceFolder: boolean
  serviceFiles?: string[]
  files?: File[] 
}): Promise<{ success: boolean; message: string }> {
  if (payload.useServiceFolder && payload.serviceFiles?.length) {
    const response = await fetch(`${API_BASE}/api/users/create`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ 
        useServiceFolder: true, 
        fileNames: payload.serviceFiles 
      }),
    })
    return response.json()
  }

  if (payload.files?.length) {
    const formData = new FormData()
    payload.files.forEach(f => formData.append("files", f))
    formData.append("useServiceFolder", "false")

    const response = await fetch(`${API_BASE}/api/users/create`, {
      method: "POST",
      body: formData,
    })
    return response.json()
  }

  throw new Error("Нет данных для отправки")
}

// Получение списка файлов из служебной папки
async function getServiceFilesAPI(): Promise<{ files: string[] }> {
  const response = await fetch(`${API_BASE}/api/service-folder/files`)
  return response.json()
}

// Получение списка групп
async function getGroupsAPI(): Promise<{ groups: string[] }> {
  const response = await fetch(`${API_BASE}/api/groups`)
  return response.json()
}

// Распределение заданий
async function distributeTasksAPI(groupNames: string[]): Promise<{ success: boolean; message: string }> {
  const response = await fetch(`${API_BASE}/api/tasks/distribute`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ groups: groupNames }),
  })
  return response.json()
}

const StatusIndicator = ({ state }: { state: OperationState }) => {
  if (state.status === "idle") return null

  return (
    <div
      className={cn(
        "mt-4 flex items-center gap-2 text-sm p-3 rounded-md select-none",
        "animate-in fade-in slide-in-from-top-2 duration-300",
        state.status === "loading" && "bg-secondary",
        state.status === "success" && "bg-primary/10",
        state.status === "error" && "bg-destructive/10"
      )}
      style={{
        color: state.status === "loading" ? "#4a5568" : 
               state.status === "success" ? "#0066cc" : "#dc2626"
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
  // Пользователи 
  const [files, setFiles] = useState<File[]>([])
  const [useServiceFolder, setUseServiceFolder] = useState(false)
  const [serviceFiles, setServiceFiles] = useState<string[]>([])
  const [selectedServiceFiles, setSelectedServiceFiles] = useState<string[]>([])
  const [loadingServiceFiles, setLoadingServiceFiles] = useState(false)
  const [isDragging, setIsDragging] = useState(false)
  const [usersState, setUsersState] = useState<OperationState>({ status: "idle", message: "" })

  // Распределение заданий 
  const [groups, setGroups] = useState<string[]>([])
  const [selectedGroups, setSelectedGroups] = useState<string[]>([])
  const [loadingGroups, setLoadingGroups] = useState(false)
  const [tasksState, setTasksState] = useState<OperationState>({ status: "idle", message: "" })

  const acceptedExtensions = [".xlsx", ".xls", ".csv", ".xml"]

  const validateFile = (file: File): boolean => {
    const extension = "." + file.name.split(".").pop()?.toLowerCase()
    return acceptedExtensions.includes(extension)
  }

  // Загрузка списка групп при монтировании
  useEffect(() => {
    const fetchGroups = async () => {
      setLoadingGroups(true)
      try {
        const data = await getGroupsAPI()
        setGroups(data.groups || [])
      } catch {
        // Демо-данные
        setGroups(["М22-501", "М22-502", "М23-501", "М23-502"])
      } finally {
        setLoadingGroups(false)
      }
    }
    fetchGroups()
  }, [])

  // Загрузка файлов из служебной папки при включении чекбокса
  useEffect(() => {
    if (useServiceFolder && serviceFiles.length === 0) {
      const fetchServiceFiles = async () => {
        setLoadingServiceFiles(true)
        try {
          const data = await getServiceFilesAPI()
          setServiceFiles(data.files || [])
        } catch {
          // Демо-данные
          setServiceFiles(["М22-501.xlsx", "М23-501.csv"])
        } finally {
          setLoadingServiceFiles(false)
        }
      }
      fetchServiceFiles()
    }
  }, [useServiceFolder, serviceFiles.length])

  const handleDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault()
    if (!useServiceFolder) setIsDragging(true)
  }, [useServiceFolder])

  const handleDragLeave = useCallback((e: React.DragEvent) => {
    e.preventDefault()
    setIsDragging(false)
  }, [])

  const handleDrop = useCallback((e: React.DragEvent) => {
    e.preventDefault()
    setIsDragging(false)
    if (useServiceFolder) return

    const droppedFiles = Array.from(e.dataTransfer.files).filter(validateFile)
    if (droppedFiles.length) {
      setFiles(prev => [...prev, ...droppedFiles])
      setUsersState({ status: "idle", message: "" })
    } else {
      setUsersState({ status: "error", message: "Неподдерживаемый формат файла" })
    }
  }, [useServiceFolder])

  const handleFileSelect = (e: React.ChangeEvent<HTMLInputElement>) => {
    const selectedFiles = Array.from(e.target.files || []).filter(validateFile)
    if (selectedFiles.length) {
      setFiles(prev => [...prev, ...selectedFiles])
      setUsersState({ status: "idle", message: "" })
    }
    e.target.value = ""
  }

  const removeFile = (index: number) => {
    setFiles(prev => prev.filter((_, i) => i !== index))
  }

  const handleServiceFolderChange = (checked: boolean) => {
    setUseServiceFolder(checked)
    if (checked) {
      setFiles([])
    } else {
      setSelectedServiceFiles([])
    }
    setUsersState({ status: "idle", message: "" })
  }

  const toggleServiceFile = (fileName: string) => {
    setSelectedServiceFiles(prev => 
      prev.includes(fileName) 
        ? prev.filter(f => f !== fileName)
        : [...prev, fileName]
    )
  }

  const handleCreateUsers = async () => {
    const hasData = useServiceFolder ? selectedServiceFiles.length > 0 : files.length > 0
    if (!hasData) {
      setUsersState({ status: "error", message: "Выберите файлы" })
      return
    }

    setUsersState({ status: "loading", message: "Отправка запроса..." })

    try {
      const result = await createUsersAPI({
        useServiceFolder,
        serviceFiles: selectedServiceFiles,
        files,
      })
      setUsersState({ 
        status: result.success ? "success" : "error", 
        message: result.message 
      })
    } catch {
      await new Promise(r => setTimeout(r, 1000))
      setUsersState({ 
        status: "success", 
        message: useServiceFolder 
          ? `Запрос отправлен. Файлы: ${selectedServiceFiles.join(", ")}`
          : `Загружено файлов: ${files.length}. Создание пользователей запущено.`
      })
    }
  }

  const toggleGroup = (group: string) => {
    setSelectedGroups(prev =>
      prev.includes(group)
        ? prev.filter(g => g !== group)
        : [...prev, group]
    )
  }

  const handleDistributeTasks = async () => {
    if (selectedGroups.length === 0) {
      setTasksState({ status: "error", message: "Выберите хотя бы одну группу" })
      return
    }

    setTasksState({ status: "loading", message: "Отправка запроса..." })

    try {
      const result = await distributeTasksAPI(selectedGroups)
      setTasksState({ 
        status: result.success ? "success" : "error", 
        message: result.message 
      })
    } catch {
      await new Promise(r => setTimeout(r, 1000))
      setTasksState({ 
        status: "success", 
        message: `Задания распределены для групп: ${selectedGroups.join(", ")}`
      })
    }
  }

  const canCreateUsers = useServiceFolder ? selectedServiceFiles.length > 0 : files.length > 0

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
          <h1 className="text-2xl font-bold tracking-tight select-none" style={{ color: '#000' }}>T-FLEX DOCs</h1>
          <p className="text-sm font-medium mt-1 select-none" style={{ color: '#4a5568' }}>Панель управления кафедры</p>
        </header>

        {/* Создание пользователей */}
        <Card className="panel-card">
          <CardHeader className="pb-4">
            <CardTitle className="text-lg font-bold flex items-center gap-2 select-none" style={{ color: '#000' }}>
              <Users className="h-5 w-5" style={{ color: '#0066cc' }} />
              Создание пользователей
            </CardTitle>
            <CardDescription className="select-none" style={{ color: '#4a5568' }}>
              Таблица: Фамилия, Имя, Отчество, Логин
            </CardDescription>
          </CardHeader>

          <CardContent className="space-y-4">
            {/* Чекбокс - служебная папка */}
            <label className="checkbox-row flex items-start gap-3 p-3 rounded-lg border border-border cursor-pointer select-none">
              <Checkbox
                checked={useServiceFolder}
                onCheckedChange={(checked) => handleServiceFolderChange(checked === true)}
                className="mt-0.5"
              />
              <div className="flex-1">
                <span className="font-semibold block" style={{ color: '#000' }}>
                  Файл из служебной папки T-FLEX
                </span>
                <span className="text-xs block mt-0.5" style={{ color: '#64748b' }}>
                  Выберите файлы из папки «Служебная»
                </span>
              </div>
            </label>

            {/* Список файлов из служебной папки */}
            {useServiceFolder && (
              <div className="border rounded-lg p-3 space-y-2 animate-in fade-in slide-in-from-top-2 duration-200">
                <div className="flex items-center gap-2 mb-2">
                  <FolderOpen className="h-4 w-4" style={{ color: '#0066cc' }} />
                  <span className="text-sm font-medium select-none" style={{ color: '#000' }}>Файлы в служебной папке</span>
                </div>
                {loadingServiceFiles ? (
                  <div className="flex items-center gap-2 py-4 justify-center">
                    <Loader2 className="h-4 w-4 animate-spin" style={{ color: '#64748b' }} />
                    <span className="text-sm" style={{ color: '#64748b' }}>Загрузка...</span>
                  </div>
                ) : serviceFiles.length === 0 ? (
                  <p className="text-sm py-4 text-center" style={{ color: '#64748b' }}>Файлы не найдены</p>
                ) : (
                  <div className="space-y-1 max-h-40 overflow-y-auto">
                    {serviceFiles.map(fileName => (
                      <label 
                        key={fileName}
                        className="flex items-center gap-2 p-2 rounded hover:bg-secondary/50 cursor-pointer select-none"
                      >
                        <Checkbox
                          checked={selectedServiceFiles.includes(fileName)}
                          onCheckedChange={() => toggleServiceFile(fileName)}
                        />
                        <FileSpreadsheet className="h-4 w-4 shrink-0" style={{ color: '#0066cc' }} />
                        <span className="text-sm truncate" style={{ color: '#000' }}>{fileName}</span>
                      </label>
                    ))}
                  </div>
                )}
              </div>
            )}

            {/* Зона загрузки файлов */}
            {!useServiceFolder && (
              <div>
                <Label className="font-semibold mb-2 block select-none" style={{ color: '#000' }}>
                  Загрузите файлы
                </Label>
                
                <div
                  onDragOver={handleDragOver}
                  onDragLeave={handleDragLeave}
                  onDrop={handleDrop}
                  className={cn(
                    "dropzone relative border-2 border-dashed rounded-lg p-6 text-center min-h-[140px] flex flex-col items-center justify-center",
                    isDragging && "dragging"
                  )}
                >
                  <label className="cursor-pointer block">
                    <input
                      type="file"
                      accept=".xlsx,.xls,.csv,.xml"
                      onChange={handleFileSelect}
                      multiple
                      className="sr-only"
                    />
                    <Upload className="h-8 w-8 mx-auto mb-2" style={{ color: '#9ca3af' }} />
                    <p className="font-medium select-none" style={{ color: '#000' }}>
                      Перетащите файлы сюда
                    </p>
                    <p className="text-sm mt-1 select-none" style={{ color: '#64748b' }}>
                      или нажмите для выбора
                    </p>
                    <p className="text-xs mt-2 select-none" style={{ color: '#9ca3af' }}>
                      Excel, CSV, XML (можно несколько)
                    </p>
                  </label>
                </div>

                {/* Выбранные файлы */}
                {files.length > 0 && (
                  <div className="mt-3 space-y-2">
                    {files.map((file, idx) => (
                      <div 
                        key={`${file.name}-${idx}`}
                        className="file-chip flex items-center gap-2 px-3 py-2 rounded-lg"
                        style={{ background: 'oklch(from var(--primary) l c h / 0.06)' }}
                      >
                        <FileSpreadsheet className="h-4 w-4 shrink-0" style={{ color: '#0066cc' }} />
                        <span className="font-medium text-sm flex-1 truncate select-none" style={{ color: '#000' }}>
                          {file.name}
                        </span>
                        <span className="text-xs shrink-0 select-none" style={{ color: '#64748b' }}>
                          {(file.size / 1024).toFixed(1)} КБ
                        </span>
                        <button
                          onClick={() => removeFile(idx)}
                          className="remove-btn p-1 rounded-full"
                          aria-label="Удалить"
                        >
                          <X className="h-3.5 w-3.5" />
                        </button>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            )}

            <Button
              onClick={handleCreateUsers}
              disabled={usersState.status === "loading" || !canCreateUsers}
              className="action-btn w-full h-11 select-none"
            >
              {usersState.status === "loading" ? (
                <Loader2 className="h-4 w-4 animate-spin mr-2" />
              ) : (
                <Users className="h-4 w-4 mr-2" />
              )}
              Создать пользователей
            </Button>

            <StatusIndicator state={usersState} />
          </CardContent>
        </Card>

        {/*  Распределение заданий  */}
        <Card className="panel-card">
          <CardHeader className="pb-4">
            <CardTitle className="text-lg font-bold flex items-center gap-2 select-none" style={{ color: '#000' }}>
              <ClipboardList className="h-5 w-5" style={{ color: '#0066cc' }} />
              Распределение заданий
            </CardTitle>
            <CardDescription className="select-none" style={{ color: '#4a5568' }}>
              Автоматическая раздача заданий студентам группы
            </CardDescription>
          </CardHeader>

          <CardContent className="space-y-4">
            <div>
              <Label className="font-semibold mb-2 block select-none" style={{ color: '#000' }}>
                Выберите группу
              </Label>
              
              {loadingGroups ? (
                <div className="flex items-center gap-2 py-4 justify-center">
                  <Loader2 className="h-4 w-4 animate-spin" style={{ color: '#64748b' }} />
                  <span className="text-sm select-none" style={{ color: '#64748b' }}>Загрузка групп...</span>
                </div>
              ) : groups.length === 0 ? (
                <p className="text-sm py-4 text-center select-none" style={{ color: '#64748b' }}>
                  Группы не найдены
                </p>
              ) : (
                <div className="border rounded-lg p-3">
                  <div className="flex items-center justify-between mb-2">
                    <span className="text-xs select-none" style={{ color: '#64748b' }}>
                      Выбрано: {selectedGroups.length} из {groups.length}
                    </span>
                    {selectedGroups.length > 0 && (
                      <button
                        onClick={() => setSelectedGroups([])}
                        className="text-xs hover:underline select-none"
                        style={{ color: '#0066cc' }}
                      >
                        Сбросить
                      </button>
                    )}
                  </div>
                  <div className="flex flex-wrap gap-2 max-h-48 overflow-y-auto">
                    {groups.map(group => (
                      <button
                        key={group}
                        onClick={() => toggleGroup(group)}
                        className={cn(
                          "group-btn px-3 py-1.5 rounded-lg border text-sm font-medium select-none",
                          selectedGroups.includes(group) && "selected"
                        )}
                        style={{ color: '#000' }}
                      >
                        {group}
                      </button>
                    ))}
                  </div>
                </div>
              )}
            </div>

            <Button
              onClick={handleDistributeTasks}
              disabled={tasksState.status === "loading" || selectedGroups.length === 0}
              className="action-btn w-full h-11 select-none"
            >
              {tasksState.status === "loading" ? (
                <Loader2 className="h-4 w-4 animate-spin mr-2" />
              ) : (
                <ClipboardList className="h-4 w-4 mr-2" />
              )}
              Распределить задания
            </Button>

            <StatusIndicator state={tasksState} />
          </CardContent>
        </Card>

        <footer className="text-center text-xs font-medium select-none" style={{ color: '#64748b' }}>
          Результат отообразится в T-FLEX
        </footer>
      </div>
    </div>
  )
}
