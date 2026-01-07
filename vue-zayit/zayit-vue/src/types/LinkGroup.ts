interface LinkGroup {
    groupName: string
    targetBookId?: number
    targetLineIndex?: number
    links: Array<{ text: string; html: string }>
}