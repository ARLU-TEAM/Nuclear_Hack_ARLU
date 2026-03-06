"use client"

import { useState } from "react"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Upload, Users, FolderPlus, FileText, CheckCircle2, Loader2, AlertCircle, Send } from "lucide-react"
import { cn } from "@/lib/utils"

type Status = "idle" | "loading" | "success" | "error"

interface OperationState {
  status: Status
  message: string
}

const StatusIndicator = ({ state }: { state: OperationState }) => {
  if (state.status === "idle") return null

  return (
    <div
      className={cn(
        "mt-4 flex items-center gap-2 text-sm p-3 rounded-md",
        "animate-in fade-in slide-in-from-top-2 duration-300",
        state.status === "loading" && "bg-muted text-muted-foreground",
        state.status === "success" && "bg-primary/10 text-primary",
        state.status === "error" && "bg-destructive/10 text-destructive"
      )}
    >
      {state.status === "loading" && <Loader2 className="h-4 w-4 animate-spin shrink-0" />}
      {state.status === "success" && <CheckCircle2 className="h-4 w-4 shrink-0" />}
      {state.status === "error" && <AlertCircle className="h-4 w-4 shrink-0" />}
      <span>{state.message}</span>
    </div>
  )
}

export default function TFlexDocsPanel() {
  const [usersFile, setUsersFile] = useState<File | null>(null)
  const [groupName, setGroupName] = useState("")
  const [taskFile, setTaskFile] = useState<File | null>(null)
  const [variantsCount, setVariantsCount] = useState("10")

  const [usersState, setUsersState] = useState<OperationState>({ status: "idle", message: "" })
  const [workspaceState, setWorkspaceState] = useState<OperationState>({ status: "idle", message: "" })
  const [taskState, setTaskState] = useState<OperationState>({ status: "idle", message: "" })
  const [assignState, setAssignState] = useState<OperationState>({ status: "idle", message: "" })

  const simulateApiCall = async (
    setState: React.Dispatch<React.SetStateAction<OperationState>>,
    successMsg: string
  ) => {
    setState({ status: "loading", message: "Отправка запроса..." })
    await new Promise(resolve => setTimeout(resolve, 1500))
    setState({ status: "success", message: successMsg })
  }

  const handleCreateUsers = async () => {
    if (!usersFile) { setUsersState({ status: "error", message: "Выберите файл" }); return }
    await simulateApiCall(setUsersState, `Файл "${usersFile.name}" отправлен. Пользователи создаются в T-FLEX.`)
  }

  const handleCreateWorkspace = async () => {
    if (!groupName.trim()) { setWorkspaceState({ status: "error", message: "Введите название группы" }); return }
    await simulateApiCall(setWorkspaceState, `Рабочее пространство для "${groupName}" создаётся в T-FLEX.`)
  }

  const handleUploadTask = async () => {
    if (!taskFile) { setTaskState({ status: "error", message: "Выберите файл задания" }); return }
    await simulateApiCall(setTaskState, `Задание "${taskFile.name}" загружено в папку заданий.`)
  }

  const handleAssignTasks = async () => {
    const count = parseInt(variantsCount)
    if (isNaN(count) || count < 1) { setAssignState({ status: "error", message: "Укажите корректное количество вариантов" }); return }
    await simulateApiCall(setAssignState, `Задания распределены по ${count} вариантам. Проверьте в T-FLEX.`)
  }

  return (
    <div className="min-h-screen bg-background p-4 md:p-8">
      <style>{`
        .tab-trigger {
          transition: color 0.2s ease, background 0.2s ease, box-shadow 0.2s ease;
        }
        .tab-trigger:hover:not([data-state="active"]) {
          color: var(--primary);
          background: oklch(from var(--primary) l c h / 0.06);
        }
        .action-btn {
          position: relative;
          overflow: hidden;
          transition: transform 0.15s ease, box-shadow 0.15s ease;
        }
        .action-btn::after {
          content: '';
          position: absolute;
          inset: 0;
          background: white;
          opacity: 0;
          transition: opacity 0.2s ease;
        }
        .action-btn:hover:not(:disabled) {
          transform: translateY(-1px);
          box-shadow: 0 4px 16px oklch(from var(--primary) l c h / 0.35);
        }
        .action-btn:hover:not(:disabled)::after {
          opacity: 0.08;
        }
        .action-btn:active:not(:disabled) {
          transform: translateY(0);
          box-shadow: none;
        }
        .file-input-wrapper {
          position: relative;
        }
        .file-input-wrapper input[type="file"] {
          transition: border-color 0.2s ease, box-shadow 0.2s ease;
        }
        .file-input-wrapper input[type="file"]:hover {
          border-color: var(--primary);
          box-shadow: 0 0 0 3px oklch(from var(--primary) l c h / 0.1);
        }
        .text-input {
          transition: border-color 0.2s ease, box-shadow 0.2s ease;
          color: #000 !important;
          font-weight: 500;
        }
        .text-input::placeholder {
          color: oklch(0.5 0.02 240) !important;
          font-weight: normal;
        }
        .text-input:hover {
          border-color: oklch(from var(--primary) l c h / 0.5);
        }
        .text-input:focus {
          box-shadow: 0 0 0 3px oklch(from var(--primary) l c h / 0.15);
        }
        .panel-card {
          transition: box-shadow 0.25s ease;
        }
        .panel-card:hover {
          box-shadow: 0 4px 24px oklch(from var(--primary) l c h / 0.08);
        }
      `}</style>

      <div className="mx-auto max-w-2xl">
        <header className="mb-8 text-center">
          <h1 className="text-3xl font-bold tracking-tight" style={{ color: '#000' }}>T-FLEX DOCs</h1>
          <p className="text-sm font-medium mt-2" style={{ color: '#4a5568' }}>Панель управления кафедры</p>
        </header>

        <Tabs defaultValue="users" className="w-full">
          <TabsList className="grid w-full grid-cols-4 mb-6">
            <TabsTrigger value="users" className="tab-trigger gap-1.5">
              <Users className="h-3.5 w-3.5 shrink-0" />
              <span className="hidden sm:inline">Пользователи</span>
              <span className="sm:hidden text-xs">Польз.</span>
            </TabsTrigger>
            <TabsTrigger value="workspace" className="tab-trigger gap-1.5">
              <FolderPlus className="h-3.5 w-3.5 shrink-0" />
              <span className="hidden sm:inline">Пространство</span>
              <span className="sm:hidden text-xs">Простр.</span>
            </TabsTrigger>
            <TabsTrigger value="tasks" className="tab-trigger gap-1.5">
              <FileText className="h-3.5 w-3.5 shrink-0" />
              <span>Задания</span>
            </TabsTrigger>
            <TabsTrigger value="assign" className="tab-trigger gap-1.5">
              <Send className="h-3.5 w-3.5 shrink-0" />
              <span>Раздать</span>
            </TabsTrigger>
          </TabsList>

          {/* Создание пользователей */}
          <TabsContent value="users" className="animate-in fade-in slide-in-from-bottom-2 duration-300">
            <Card className="panel-card">
              <CardHeader>
                <CardTitle className="text-lg font-bold" style={{ color: '#000' }}>Создание пользователей</CardTitle>
                <CardDescription style={{ color: '#4a5568' }}>
                  Загрузите файл со списком студентов — Excel, CSV или XML
                </CardDescription>
              </CardHeader>
              <CardContent className="space-y-4">
                <div>
                  <Label htmlFor="users-file" className="font-semibold" style={{ color: '#000' }}>Файл со списком</Label>
                  <div className="file-input-wrapper mt-1.5">
                    <Input
                      id="users-file"
                      type="file"
                      accept=".xlsx,.xls,.csv,.xml"
                      onChange={(e) => {
                        setUsersFile(e.target.files?.[0] || null)
                        setUsersState({ status: "idle", message: "" })
                      }}
                      className="cursor-pointer"
                    />
                  </div>
                  <p className="text-xs text-muted-foreground mt-1.5">
                    Формат: ФИО, группа, email (опционально)
                  </p>
                </div>
                <Button
                  onClick={handleCreateUsers}
                  disabled={usersState.status === "loading"}
                  className="action-btn w-full"
                >
                  {usersState.status === "loading"
                    ? <Loader2 className="h-4 w-4 animate-spin mr-2" />
                    : <Upload className="h-4 w-4 mr-2" />}
                  Создать пользователей
                </Button>
                <StatusIndicator state={usersState} />
              </CardContent>
            </Card>
          </TabsContent>

          {/* Создание рабочего пространства */}
          <TabsContent value="workspace" className="animate-in fade-in slide-in-from-bottom-2 duration-300">
            <Card className="panel-card">
              <CardHeader>
                <CardTitle className="text-lg font-bold" style={{ color: '#000' }}>Рабочее пространство</CardTitle>
                <CardDescription style={{ color: '#4a5568' }}>
                  Создание структуры папок для учебной группы
                </CardDescription>
              </CardHeader>
              <CardContent className="space-y-4">
                <div>
                  <Label htmlFor="group-name" className="font-semibold" style={{ color: '#000' }}>Название группы</Label>
                  <Input
                    id="group-name"
                    placeholder="Например: М22-501"
                    value={groupName}
                    onChange={(e) => {
                      setGroupName(e.target.value)
                      setWorkspaceState({ status: "idle", message: "" })
                    }}
                    className="text-input mt-1.5"
                  />
                  <p className="text-xs text-muted-foreground mt-1.5">
                    Будет создана папка группы со структурой для каждого студента
                  </p>
                </div>
                <Button
                  onClick={handleCreateWorkspace}
                  disabled={workspaceState.status === "loading"}
                  className="action-btn w-full"
                >
                  {workspaceState.status === "loading"
                    ? <Loader2 className="h-4 w-4 animate-spin mr-2" />
                    : <FolderPlus className="h-4 w-4 mr-2" />}
                  Создать пространство
                </Button>
                <StatusIndicator state={workspaceState} />
              </CardContent>
            </Card>
          </TabsContent>

          {/* Загрузка заданий */}
          <TabsContent value="tasks" className="animate-in fade-in slide-in-from-bottom-2 duration-300">
            <Card className="panel-card">
              <CardHeader>
                <CardTitle className="text-lg font-bold" style={{ color: '#000' }}>Загрузка задания</CardTitle>
                <CardDescription style={{ color: '#4a5568' }}>
                  Добавление задания в общую папку заданий T-FLEX
                </CardDescription>
              </CardHeader>
              <CardContent className="space-y-4">
                <div>
                  <Label htmlFor="task-file" className="font-semibold" style={{ color: '#000' }}>Файл задания</Label>
                  <div className="file-input-wrapper mt-1.5">
                    <Input
                      id="task-file"
                      type="file"
                      accept=".grb,.pdf,.dwg,.dxf"
                      onChange={(e) => {
                        setTaskFile(e.target.files?.[0] || null)
                        setTaskState({ status: "idle", message: "" })
                      }}
                      className="cursor-pointer"
                    />
                  </div>
                  <p className="text-xs text-muted-foreground mt-1.5">
                    Поддерживаемые форматы: GRB, PDF, DWG, DXF
                  </p>
                </div>
                <Button
                  onClick={handleUploadTask}
                  disabled={taskState.status === "loading"}
                  className="action-btn w-full"
                >
                  {taskState.status === "loading"
                    ? <Loader2 className="h-4 w-4 animate-spin mr-2" />
                    : <FileText className="h-4 w-4 mr-2" />}
                  Загрузить задание
                </Button>
                <StatusIndicator state={taskState} />
              </CardContent>
            </Card>
          </TabsContent>

          {/* Распределение заданий */}
          <TabsContent value="assign" className="animate-in fade-in slide-in-from-bottom-2 duration-300">
            <Card className="panel-card">
              <CardHeader>
                <CardTitle className="text-lg font-bold" style={{ color: '#000' }}>Распределение заданий</CardTitle>
                <CardDescription style={{ color: '#4a5568' }}>
                  Автоматическая раздача вариантов студентам группы
                </CardDescription>
              </CardHeader>
              <CardContent className="space-y-4">
                <div>
                  <Label htmlFor="variants" className="font-semibold" style={{ color: '#000' }}>Количество вариантов</Label>
                  <Input
                    id="variants"
                    type="number"
                    min="1"
                    max="100"
                    value={variantsCount}
                    onChange={(e) => {
                      setVariantsCount(e.target.value)
                      setAssignState({ status: "idle", message: "" })
                    }}
                    className="text-input mt-1.5"
                  />
                  <p className="text-xs text-muted-foreground mt-1.5">
                    Варианты распределяются циклически по списку студентов
                  </p>
                </div>
                <Button
                  onClick={handleAssignTasks}
                  disabled={assignState.status === "loading"}
                  className="action-btn w-full"
                >
                  {assignState.status === "loading"
                    ? <Loader2 className="h-4 w-4 animate-spin mr-2" />
                    : <Send className="h-4 w-4 mr-2" />}
                  Раздать задания
                </Button>
                <StatusIndicator state={assignState} />
              </CardContent>
            </Card>
          </TabsContent>
        </Tabs>

        <footer className="mt-8 text-center text-xs font-medium" style={{ color: '#4a5568' }}>
          Результаты операций отображаются в T-FLEX DOCs
        </footer>
      </div>
    </div>
  )
}
