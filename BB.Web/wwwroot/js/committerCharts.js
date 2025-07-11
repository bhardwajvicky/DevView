// Use window scope to avoid variable redeclaration issues
window.committerCharts = {};
window.topCommittersDotNetRef = null;

window.setTopCommittersDotNetRef = (dotNetRef) => {
    window.topCommittersDotNetRef = dotNetRef;
};

window.openExternalUrl = (url, target = '_blank') => {
    try {
        const newWindow = window.open(url, target);
        if (newWindow) {
            newWindow.focus();
            console.log(`Successfully opened URL: ${url}`);
        } else {
            console.warn(`Failed to open URL: ${url}. Pop-up blocked or similar issue.`);
            alert(`Failed to open link. Please check your browser's pop-up blocker settings or try again.\n\nURL: ${url}`);
        }
    } catch (error) {
        console.error(`Error opening URL ${url}:`, error);
        alert(`An error occurred while trying to open the link. Please try again.\n\nURL: ${url}`);
    }
};

// Function to wait for DOM element with retries
window.waitForElement = (elementId, maxRetries = 10, retryDelay = 100) => {
    return new Promise((resolve, reject) => {
        let retries = 0;
        
        const checkElement = () => {
            const element = document.getElementById(elementId);
            if (element) {
                resolve(element);
            } else if (retries < maxRetries) {
                retries++;
                // console.log(`Waiting for element ${elementId}, retry ${retries}/${maxRetries}`); // Commented out for less noise
                setTimeout(checkElement, retryDelay);
            } else {
                reject(new Error(`Element ${elementId} not found after ${maxRetries} retries`));
            }
        };
        
        checkElement();
    });
};

window.initializeCommitterChart = async (canvasId, rawData, displayName, isTopCommitter) => {
    try {
        // console.log('Initializing chart:', canvasId, 'Raw Data:', rawData); // Commented out for less noise
        
        // Parse data if it's a string
        let data;
        try {
            data = typeof rawData === 'string' ? JSON.parse(rawData) : rawData;
            // console.log('Parsed data:', data); // Commented out for less noise
        } catch (parseError) {
            console.error('Error parsing data:', parseError);
            return;
        }
        
        // Validate data
        if (!Array.isArray(data)) {
            console.error('Data is not an array after parsing:', typeof data, data);
            return;
        }
        
        if (data.length === 0) {
            console.warn('Data array is empty for canvas:', canvasId);
            return;
        }

        // Wait for Chart.js to be loaded
        if (typeof Chart === 'undefined') {
            console.warn('Chart.js not loaded, waiting...');
            await new Promise(resolve => setTimeout(resolve, 500));
            if (typeof Chart === 'undefined') {
                throw new Error('Chart.js failed to load');
            }
        }

        // Wait for the canvas element
        const chartElement = await window.waitForElement(canvasId);
        
        // Destroy existing chart if it exists
        if (window.committerCharts[canvasId] && typeof window.committerCharts[canvasId].destroy === 'function') {
            window.committerCharts[canvasId].destroy();
            delete window.committerCharts[canvasId];
        }

        // Format dates based on grouping
        const grouping = window.selectedGrouping || 'Day';
        const labels = data.map(d => {
            const date = new Date(d.date);
            if (isNaN(date.getTime())) {
                console.warn('Invalid date:', d.date);
                return 'Invalid Date';
            }
            switch (grouping) {
                case 'Month':
                    return date.toLocaleDateString('en-US', { month: 'short', year: 'numeric' });
                case 'Week':
                    return date.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
                case 'Day':
                default:
                    return date.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
            }
        });

        // Prepare data with proper scaling
        const commitCounts = data.map(d => d.commitCount || 0);
        const totalAdded = data.map(d => d.totalLinesAdded || 0);
        const totalRemoved = data.map(d => d.totalLinesRemoved || 0);
        const codeAdded = data.map(d => d.codeLinesAdded || 0);
        const codeRemoved = data.map(d => d.codeLinesRemoved || 0);
        const dataAdded = data.map(d => d.dataLinesAdded || 0);
        const dataRemoved = data.map(d => d.dataLinesRemoved || 0);
        const configAdded = data.map(d => d.configLinesAdded || 0);
        const configRemoved = data.map(d => d.configLinesRemoved || 0);

        // Create chart with Dashboard-style configuration
        const ctx = chartElement.getContext('2d');
        window.committerCharts[canvasId] = new Chart(ctx, {
            type: 'line',
            data: {
                labels: labels,
                datasets: [
                    {
                        label: 'Commits',
                        data: commitCounts,
                        borderColor: 'rgb(54, 162, 235)',
                        backgroundColor: 'rgba(54, 162, 235, 0.1)',
                        fill: true,
                        tension: 0.4,
                        pointRadius: 3,
                        pointHoverRadius: 6,
                        borderWidth: 2,
                        hidden: false
                    },
                    {
                        label: 'Total ++',
                        data: totalAdded,
                        borderColor: 'rgb(34, 197, 94)',
                        backgroundColor: 'rgba(34, 197, 94, 0.1)',
                        fill: false,
                        tension: 0.4,
                        pointRadius: 2,
                        pointHoverRadius: 5,
                        borderWidth: 1.5,
                        hidden: false
                    },
                    {
                        label: 'Total --',
                        data: totalRemoved,
                        borderColor: 'rgb(239, 68, 68)',
                        backgroundColor: 'rgba(239, 68, 68, 0.1)',
                        fill: false,
                        tension: 0.4,
                        pointRadius: 2,
                        pointHoverRadius: 5,
                        borderWidth: 1.5,
                        hidden: false
                    },
                    {
                        label: '🧑‍💻 Code ++',
                        data: codeAdded,
                        borderColor: 'rgb(22, 163, 74)',
                        backgroundColor: 'rgba(22, 163, 74, 0.1)',
                        fill: false,
                        tension: 0.4,
                        pointRadius: 2,
                        pointHoverRadius: 5,
                        borderWidth: 1.5,
                        hidden: true
                    },
                    {
                        label: '🧑‍💻 Code --',
                        data: codeRemoved,
                        borderColor: 'rgb(220, 38, 38)',
                        backgroundColor: 'rgba(220, 38, 38, 0.1)',
                        fill: false,
                        tension: 0.4,
                        pointRadius: 2,
                        pointHoverRadius: 5,
                        borderWidth: 1.5,
                        hidden: true
                    },
                    {
                        label: '🗄️ Data ++',
                        data: dataAdded,
                        borderColor: 'rgb(147, 51, 234)',
                        backgroundColor: 'rgba(147, 51, 234, 0.1)',
                        fill: false,
                        tension: 0.4,
                        pointRadius: 2,
                        pointHoverRadius: 5,
                        borderWidth: 1.5,
                        hidden: true
                    },
                    {
                        label: '🗄️ Data --',
                        data: dataRemoved,
                        borderColor: 'rgb(126, 34, 206)',
                        backgroundColor: 'rgba(126, 34, 206, 0.1)',
                        fill: false,
                        tension: 0.4,
                        pointRadius: 2,
                        pointHoverRadius: 5,
                        borderWidth: 1.5,
                        hidden: true
                    },
                    {
                        label: '🛠️ Config ++',
                        data: configAdded,
                        borderColor: 'rgb(245, 158, 11)',
                        backgroundColor: 'rgba(245, 158, 11, 0.1)',
                        fill: false,
                        tension: 0.4,
                        pointRadius: 2,
                        pointHoverRadius: 5,
                        borderWidth: 1.5,
                        hidden: true
                    },
                    {
                        label: '🛠️ Config --',
                        data: configRemoved,
                        borderColor: 'rgb(217, 119, 6)',
                        backgroundColor: 'rgba(217, 119, 6, 0.1)',
                        fill: false,
                        tension: 0.4,
                        pointRadius: 2,
                        pointHoverRadius: 5,
                        borderWidth: 1.5,
                        hidden: true
                    }
                ]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: {
                        display: true,
                        position: 'bottom',
                        labels: {
                            usePointStyle: true,
                            pointStyle: 'circle',
                            font: {
                                size: 10
                            },
                            padding: 10
                        }
                    },
                    tooltip: {
                        callbacks: {
                            title: function(context) {
                                const dataIndex = context[0].dataIndex;
                                const date = new Date(data[dataIndex].date);
                                return date.toLocaleDateString('en-US', { 
                                    weekday: 'short', 
                                    month: 'short', 
                                    day: 'numeric' 
                                });
                            },
                            label: function(context) {
                                const dataIndex = context.dataIndex;
                                const item = data[dataIndex];
                                const label = context.dataset.label;
                                switch(label) {
                                    case 'Commits':
                                        return `📝 Commits: ${item.commitCount}`;
                                    case 'Total ++':
                                        return `➕ Total ++: ${item.totalLinesAdded.toLocaleString()} lines`;
                                    case 'Total --':
                                        return `➖ Total --: ${item.totalLinesRemoved.toLocaleString()} lines`;
                                    case '🧑‍💻 Code ++':
                                        return `🧑‍💻 Code ++: ${item.codeLinesAdded.toLocaleString()} lines`;
                                    case '🧑‍💻 Code --':
                                        return `🧑‍💻 Code --: ${item.codeLinesRemoved.toLocaleString()} lines`;
                                    case '🗄️ Data ++':
                                        return `🗄️ Data ++: ${item.dataLinesAdded.toLocaleString()} lines`;
                                    case '🗄️ Data --':
                                        return `🗄️ Data --: ${item.dataLinesRemoved.toLocaleString()} lines`;
                                    case '🛠️ Config ++':
                                        return `🛠️ Config ++: ${item.configLinesAdded.toLocaleString()} lines`;
                                    case '🛠️ Config --':
                                        return `🛠️ Config --: ${item.configLinesRemoved.toLocaleString()} lines`;
                                    default:
                                        return `${label}: ${context.parsed.y.toLocaleString()}`;
                                }
                            }
                        }
                    }
                },
                scales: {
                    x: {
                        display: true,
                        grid: {
                            display: false
                        },
                        ticks: {
                            maxTicksLimit: 6,
                            font: {
                                size: 10
                            }
                        }
                    },
                    y: {
                        display: true,
                        beginAtZero: true,
                        grid: {
                            color: 'rgba(0, 0, 0, 0.1)'
                        },
                        ticks: {
                            font: {
                                size: 10
                            }
                        }
                    }
                }
            }
        });
        
        // console.log(`Successfully initialized chart for ${canvasId}`); // Commented out for less noise
    } catch (error) {
        console.error('Error initializing chart:', canvasId, error);
    }
};

window.toggleCommitterDataset = function(canvasId, datasetIndex) {
    const chart = window.committerCharts[canvasId];
    if (!chart) {
        console.warn('No chart found for', canvasId);
        return;
    }
    const dataset = chart.data.datasets[datasetIndex];
    dataset.hidden = !dataset.hidden;
    chart.update();
}; 