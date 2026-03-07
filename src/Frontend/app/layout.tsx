import type { Metadata } from 'next'
import { Geist, Geist_Mono } from 'next/font/google'
import './globals.css'

const _geist = Geist({ subsets: ["latin"] });
const _geistMono = Geist_Mono({ subsets: ["latin"] });

export const metadata: Metadata = {
  title: 'Цифровая кафедра | T-FLEX DOCs',
  description: 'Система автоматизации рабочих процессов кафедры инженерной графики НИЯУ МИФИ',
  icons: {
    icon: [

      {
        url: '/favicon.png',
        media: '(prefers-color-scheme: dark)',
      }
    ],
  },
}

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode
}>) {
  return (
    <html lang="ru" suppressHydrationWarning>
      <body className="font-sans antialiased">
        {children}
      </body>
    </html>
  )
}
